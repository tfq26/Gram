import crypto from 'node:crypto';
import type { Database } from 'bun:sqlite';

export type KnowledgeScope = 'global' | 'project';
export type KnowledgeKind =
  | 'preference'
  | 'correction'
  | 'architecture'
  | 'workflow'
  | 'gotcha'
  | 'pattern';

export interface KnowledgeNode {
  id: string;
  scope: KnowledgeScope;
  projectId: string | null;
  kind: KnowledgeKind;
  title: string;
  content: string;
  tags: string[];
  supersededBy: string | null;
  confidence: number;
  sourceModel: string;
  hitCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface KnowledgeRetrievalResult {
  items: Array<KnowledgeNode & { score: number }>;
  formattedPromptBlock: string;
}

const STOP_WORDS = new Set([
  'the', 'and', 'for', 'with', 'that', 'this', 'from', 'into', 'your', 'you',
  'are', 'was', 'were', 'have', 'has', 'had', 'will', 'would', 'could', 'should',
  'can', 'not', 'but', 'what', 'when', 'where', 'which', 'who', 'how', 'why',
  'want', 'need', 'just', 'like', 'make', 'build', 'create', 'please', 'help',
  'use', 'using', 'used', 'about', 'there', 'here', 'some', 'more', 'also',
]);

function tokenize(text: string): string[] {
  return Array.from(
    new Set(
      text
        .toLowerCase()
        .replace(/[^a-z0-9_./-]+/g, ' ')
        .split(/\s+/)
        .map((t) => t.trim())
        .filter((t) => t.length >= 2 && !STOP_WORDS.has(t))
    )
  );
}

function jaccardOverlap(a: string[], b: string[]): number {
  if (a.length === 0 || b.length === 0) return 0;
  const setB = new Set(b);
  let shared = 0;
  for (const token of a) {
    if (setB.has(token)) shared++;
  }
  return shared / (a.length + b.length - shared);
}

function rowToNode(row: any): KnowledgeNode {
  let tags: string[] = [];
  try {
    const parsed = JSON.parse(row.tags_json || '[]');
    if (Array.isArray(parsed)) tags = parsed.map(String);
  } catch {}
  return {
    id: String(row.id),
    scope: row.scope === 'project' ? 'project' : 'global',
    projectId: row.project_id ? String(row.project_id) : null,
    kind: row.kind as KnowledgeKind,
    title: String(row.title),
    content: String(row.content),
    tags,
    supersededBy: row.superseded_by ? String(row.superseded_by) : null,
    confidence: Number(row.confidence ?? 0.85),
    sourceModel: String(row.source_model || 'system'),
    hitCount: Number(row.hit_count ?? 0),
    createdAt: String(row.created_at),
    updatedAt: String(row.updated_at),
  };
}

/**
 * Lean, high-signal Knowledge Store shared across models and projects.
 */
export class KnowledgeGraphStore {
  private ftsEnabled = false;

  constructor(private db: Database) {
    this.initSchema();
  }

  private initSchema(): void {
    this.db.run(`
      CREATE TABLE IF NOT EXISTS knowledge_nodes (
        id TEXT PRIMARY KEY,
        scope TEXT NOT NULL,
        project_id TEXT,
        kind TEXT NOT NULL,
        title TEXT NOT NULL,
        content TEXT NOT NULL,
        tags_json TEXT NOT NULL DEFAULT '[]',
        superseded_by TEXT,
        confidence REAL NOT NULL DEFAULT 0.85,
        source_model TEXT NOT NULL DEFAULT 'system',
        hit_count INTEGER NOT NULL DEFAULT 0,
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL
      );
    `);

    try {
      this.db.run(`ALTER TABLE knowledge_nodes ADD COLUMN superseded_by TEXT`);
    } catch {}

    this.db.run(`CREATE INDEX IF NOT EXISTS idx_knowledge_scope_project ON knowledge_nodes(scope, project_id);`);

    try {
      this.db.run(`
        CREATE VIRTUAL TABLE IF NOT EXISTS knowledge_fts USING fts5(
          node_id UNINDEXED,
          title,
          content,
          tags_text
        );
      `);
      this.ftsEnabled = true;
    } catch {
      this.ftsEnabled = false;
    }
  }

  private upsertFts(node: KnowledgeNode): void {
    if (!this.ftsEnabled) return;
    try {
      this.db.run(`DELETE FROM knowledge_fts WHERE node_id = ?`, [node.id]);
      this.db.run(
        `INSERT INTO knowledge_fts (node_id, title, content, tags_text) VALUES (?, ?, ?, ?)`,
        [node.id, node.title, node.content, node.tags.join(' ')]
      );
    } catch {}
  }

  public recordInsight(input: {
    id?: string;
    scope?: KnowledgeScope;
    projectId?: string | null;
    kind: KnowledgeKind;
    title: string;
    content: string;
    tags?: string[];
    confidence?: number;
    sourceModel?: string;
    supersedesId?: string;
  }): { node: KnowledgeNode; created: boolean } {
    const scope: KnowledgeScope = input.scope === 'project' && input.projectId ? 'project' : 'global';
    const projectId = scope === 'project' ? input.projectId || null : null;
    const title = input.title.trim().slice(0, 180);
    const content = input.content.trim().slice(0, 1200);
    const tags = Array.from(
      new Set([
        ...(input.tags || []).map((t) => t.toLowerCase().trim()).filter(Boolean),
        ...tokenize(`${title} ${content}`).slice(0, 8),
      ])
    ).slice(0, 14);
    const nowIso = new Date().toISOString();

    const candidates = this.db
      .query<any, [string, string | null, string | null]>(
        `SELECT * FROM knowledge_nodes WHERE superseded_by IS NULL AND scope = ? AND (project_id IS ? OR project_id = ?) LIMIT 60`
      )
      .all(scope, projectId, projectId);

    const incomingTokens = tokenize(`${title} ${content}`);
    for (const row of candidates) {
      const existing = rowToNode(row);
      const sim = jaccardOverlap(incomingTokens, tokenize(`${existing.title} ${existing.content}`));
      if (existing.title.toLowerCase() === title.toLowerCase() || sim >= 0.72) {
        const mergedTags = Array.from(new Set([...existing.tags, ...tags])).slice(0, 16);
        const nextConfidence = Math.min(1.0, Math.max(existing.confidence, input.confidence ?? 0.86) + 0.04);
        this.db.run(
          `UPDATE knowledge_nodes
           SET content = ?, tags_json = ?, confidence = ?, source_model = ?, updated_at = ?
           WHERE id = ?`,
          [
            content.length >= existing.content.length ? content : existing.content,
            JSON.stringify(mergedTags),
            nextConfidence,
            input.sourceModel || existing.sourceModel,
            nowIso,
            existing.id,
          ]
        );
        const updated = rowToNode(
          this.db.query<any, [string]>(`SELECT * FROM knowledge_nodes WHERE id = ?`).get(existing.id)
        );
        this.upsertFts(updated);
        return { node: updated, created: false };
      }
    }

    const node: KnowledgeNode = {
      id: input.id || `kn-${crypto.randomUUID().slice(0, 12)}`,
      scope,
      projectId,
      kind: input.kind,
      title,
      content,
      tags,
      supersededBy: null,
      confidence: Math.min(1.0, Math.max(0.1, input.confidence ?? 0.86)),
      sourceModel: input.sourceModel || 'unknown',
      hitCount: 0,
      createdAt: nowIso,
      updatedAt: nowIso,
    };

    this.db.run(
      `INSERT INTO knowledge_nodes
       (id, scope, project_id, kind, title, content, tags_json, superseded_by, confidence, source_model, hit_count, created_at, updated_at)
       VALUES (?, ?, ?, ?, ?, ?, ?, NULL, ?, ?, 0, ?, ?)`,
      [
        node.id,
        node.scope,
        node.projectId,
        node.kind,
        node.title,
        node.content,
        JSON.stringify(node.tags),
        node.confidence,
        node.sourceModel,
        node.createdAt,
        node.updatedAt,
      ]
    );
    this.upsertFts(node);

    if (input.supersedesId) {
      this.db.run(`UPDATE knowledge_nodes SET superseded_by = ?, updated_at = ? WHERE id = ?`, [
        node.id,
        nowIso,
        input.supersedesId,
      ]);
    }

    return { node, created: true };
  }

  public retrieveRelevantKnowledge(params: {
    query: string;
    projectId?: string | null;
    limit?: number;
  }): KnowledgeRetrievalResult {
    const limit = Math.max(1, Math.min(10, params.limit ?? 6));
    const projectId = params.projectId || null;

    const candidateRows = this.db
      .query<any, [string | null]>(
        `SELECT * FROM knowledge_nodes
         WHERE superseded_by IS NULL AND (scope = 'global' OR (scope = 'project' AND project_id = ?))
         ORDER BY updated_at DESC
         LIMIT 200`
      )
      .all(projectId);

    if (candidateRows.length === 0) {
      return { items: [], formattedPromptBlock: '' };
    }

    const queryTokens = tokenize(params.query);
    const ftsRankBoost = new Map<string, number>();
    if (this.ftsEnabled && queryTokens.length > 0) {
      try {
        const ftsExpr = queryTokens
          .slice(0, 8)
          .map((t) => `"${t.replace(/"/g, '')}"`)
          .join(' OR ');
        const ftsHits = this.db
          .query<{ node_id: string }, [string]>(
            `SELECT node_id FROM knowledge_fts WHERE knowledge_fts MATCH ? ORDER BY bm25(knowledge_fts) LIMIT 25`
          )
          .all(ftsExpr);
        ftsHits.forEach((hit, idx) => {
          ftsRankBoost.set(String(hit.node_id), Math.max(0.1, 1.0 - idx * 0.04));
        });
      } catch {}
    }

    const queryTokenSet = new Set(queryTokens);
    const items = candidateRows
      .map(rowToNode)
      .map((node) => {
        const nodeTokens = tokenize(`${node.title} ${node.content} ${node.tags.join(' ')}`);
        const overlapCount = nodeTokens.filter((t) => queryTokenSet.has(t)).length;
        const lexicalScore = queryTokenSet.size > 0 ? overlapCount / Math.sqrt(queryTokenSet.size) : 0;
        const ftsScore = ftsRankBoost.get(node.id) || 0;
        const kindBoost =
          node.kind === 'correction' ? 0.35 : node.kind === 'preference' ? 0.28 : node.kind === 'workflow' ? 0.2 : 0.12;
        const scopeBoost = node.scope === 'project' && projectId && node.projectId === projectId ? 0.3 : 0.08;
        const score = ftsScore * 0.9 + lexicalScore * 0.9 + node.confidence * 0.25 + kindBoost + scopeBoost;
        return { ...node, score };
      })
      .sort((a, b) => b.score - a.score)
      .slice(0, limit);

    for (const item of items) {
      this.db.run(`UPDATE knowledge_nodes SET hit_count = hit_count + 1 WHERE id = ?`, [item.id]);
    }

    const lines = items.map((node) => `- [${node.scope}/${node.kind}] ${node.title}: ${node.content}`);
    const formattedPromptBlock =
      lines.length > 0
        ? `<INHERITED_KNOWLEDGE>\nDurable preferences, corrections, and facts shared across models and projects:\n${lines.join('\n')}\n</INHERITED_KNOWLEDGE>`
        : '';

    return { items, formattedPromptBlock };
  }

  /**
   * Captures only explicit, durable user directives (never casual debugging chatter).
   */
  public captureExplicitDirective(params: {
    userPrompt: string;
    projectId?: string | null;
    modelId?: string;
  }): KnowledgeNode[] {
    const prompt = params.userPrompt.trim();
    if (prompt.length < 18 || prompt.length > 1500) return [];
    const lower = prompt.toLowerCase();

    const isExplicitDirective =
      /\b(remember that|remember this|from now on|always use|never use|for all projects|i prefer using)\b/i.test(
        lower
      );
    if (!isExplicitDirective) return [];

    const kind: KnowledgeKind = /\b(never|don't|stop)\b/i.test(lower) ? 'correction' : 'preference';
    const { node } = this.recordInsight({
      scope: 'global',
      projectId: params.projectId,
      kind,
      title: `${kind === 'correction' ? 'Rule' : 'Preference'}: ${prompt.slice(0, 90).replace(/\s+/g, ' ')}`,
      content: prompt.slice(0, 600),
      sourceModel: params.modelId || 'user-directive',
      confidence: 0.94,
    });
    return [node];
  }

  public listNodes(scope?: KnowledgeScope, projectId?: string | null, limit = 50): KnowledgeNode[] {
    if (scope === 'project' && projectId) {
      return this.db
        .query<any, [string, number]>(
          `SELECT * FROM knowledge_nodes WHERE superseded_by IS NULL AND scope = 'project' AND project_id = ? ORDER BY updated_at DESC LIMIT ?`
        )
        .all(projectId, limit)
        .map(rowToNode);
    }
    if (scope === 'global') {
      return this.db
        .query<any, [number]>(
          `SELECT * FROM knowledge_nodes WHERE superseded_by IS NULL AND scope = 'global' ORDER BY updated_at DESC LIMIT ?`
        )
        .all(limit)
        .map(rowToNode);
    }
    return this.db
      .query<any, [number]>(
        `SELECT * FROM knowledge_nodes WHERE superseded_by IS NULL ORDER BY updated_at DESC LIMIT ?`
      )
      .all(limit)
      .map(rowToNode);
  }
}

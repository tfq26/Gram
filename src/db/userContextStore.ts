import crypto from 'node:crypto';
import type { Database } from 'bun:sqlite';

export type UserContextCategory =
  | 'communication'
  | 'coding'
  | 'workflow'
  | 'career'
  | 'family'
  | 'finances'
  | 'relationships'
  | 'health'
  | 'education'
  | 'background'
  | 'goals'
  | 'other';

export interface UserContextItem {
  id: string;
  category: UserContextCategory;
  key: string;
  value: string;
  source: 'explicit' | 'manual' | 'imported';
  evidence: string | null;
  confidence: number;
  sourceSessionId: string | null;
  createdAt: string;
  updatedAt: string;
  lastConfirmedAt: string;
}

export interface UserContextProfile {
  schemaVersion: 1;
  enabled: boolean;
  autoCapture: boolean;
  updatedAt: string;
  items: UserContextItem[];
}

export interface UserContextCandidate {
  category: UserContextCategory;
  key: string;
  value: string;
  evidence: string;
  sessionId: string;
  chatTitle: string;
}

function rowToItem(row: any): UserContextItem {
  const validCategories = new Set<UserContextCategory>([
    'communication', 'coding', 'workflow', 'career', 'family', 'finances',
    'relationships', 'health', 'education', 'background', 'goals', 'other',
  ]);
  const category = String(row.category) as UserContextCategory;
  return {
    id: String(row.id),
    category: validCategories.has(category) ? category : 'other',
    key: String(row.context_key),
    value: String(row.value),
    source: row.source === 'manual' || row.source === 'imported' ? row.source : 'explicit',
    evidence: row.evidence_text ? String(row.evidence_text) : null,
    confidence: Number(row.confidence ?? 1),
    sourceSessionId: row.source_session_id ? String(row.source_session_id) : null,
    createdAt: String(row.created_at),
    updatedAt: String(row.updated_at),
    lastConfirmedAt: String(row.last_confirmed_at),
  };
}

const USER_CONTEXT_CATEGORIES = new Set<UserContextCategory>([
  'communication', 'coding', 'workflow', 'career', 'family', 'finances',
  'relationships', 'health', 'education', 'background', 'goals', 'other',
]);

function classifyDirective(value: string): { category: UserContextCategory; key: string } {
  if (/\b(answer|response|explanation|writing|tone|style|concise|brief|detailed|format)\b/i.test(value)) {
    return { category: 'communication', key: 'communication.response_style' };
  }
  if (/\b(code|coding|program|language|typescript|javascript|python|java|c#|rust|go|test|tests)\b/i.test(value)) {
    return { category: 'coding', key: 'coding.preferences' };
  }
  if (/\b(workflow|process|approach|when i|ask me|before you|from now on)\b/i.test(value)) {
    return { category: 'workflow', key: 'workflow.preferences' };
  }
  return { category: 'other', key: `preference.${crypto.createHash('sha1').update(value.toLowerCase()).digest('hex').slice(0, 12)}` };
}

function isCredentialLike(value: string): boolean {
  return /(?:sk-[A-Za-z0-9_-]{20,}|gh[pousr]_[A-Za-z0-9]{20,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|\b(?:password|api[_ -]?key|access[_ -]?token)\s*(?:is|:|=)\s*\S+|\b\d{3}-\d{2}-\d{4}\b|\b(?:bank account|routing|credit card|debit card) number\b)/i.test(value);
}

/** Local, user-level profile storage. Project facts stay in KnowledgeGraphStore. */
export class UserContextStore {
  constructor(private readonly db: Database) {
    this.db.run(`
      CREATE TABLE IF NOT EXISTS user_context_items (
        id TEXT PRIMARY KEY,
        category TEXT NOT NULL,
        context_key TEXT NOT NULL,
        value TEXT NOT NULL,
        source TEXT NOT NULL DEFAULT 'explicit',
        evidence_text TEXT,
        confidence REAL NOT NULL DEFAULT 1,
        source_session_id TEXT,
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL,
        last_confirmed_at TEXT NOT NULL,
        UNIQUE (category, context_key)
      );
    `);
    try {
      this.db.run(`ALTER TABLE user_context_items ADD COLUMN evidence_text TEXT`);
    } catch {}
    // Invariant: Verify the composite UNIQUE(category, context_key) index via SQLite PRAGMA
    // metadata (not regex over CREATE TABLE SQL) so legacy schemas always migrate reliably.
    const indexList = this.db
      .query<{ name: string; unique: number }, []>(`PRAGMA index_list('user_context_items')`)
      .all();
    const hasCompositeCategoryKeyUnique = indexList.some((idx) => {
      if (idx.unique !== 1) return false;
      const cols = this.db
        .query<{ name: string }, []>(`PRAGMA index_info('${idx.name.replace(/'/g, "''")}')`)
        .all()
        .map((c) => c.name);
      return cols.length === 2 && cols[0] === 'category' && cols[1] === 'context_key';
    });
    if (!hasCompositeCategoryKeyUnique) {
      this.db.run('BEGIN IMMEDIATE');
      try {
        this.db.run(`
          CREATE TABLE user_context_items_migrated (
            id TEXT PRIMARY KEY,
            category TEXT NOT NULL,
            context_key TEXT NOT NULL,
            value TEXT NOT NULL,
            source TEXT NOT NULL DEFAULT 'explicit',
            evidence_text TEXT,
            confidence REAL NOT NULL DEFAULT 1,
            source_session_id TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            last_confirmed_at TEXT NOT NULL,
            UNIQUE (category, context_key)
          );
        `);
        this.db.run(`
          INSERT OR IGNORE INTO user_context_items_migrated
            (id, category, context_key, value, source, evidence_text, confidence, source_session_id, created_at, updated_at, last_confirmed_at)
          SELECT
            id, category, context_key, value, source, evidence_text, confidence, source_session_id, created_at, updated_at, last_confirmed_at
          FROM user_context_items;
        `);
        this.db.run(`DROP TABLE user_context_items;`);
        this.db.run(`ALTER TABLE user_context_items_migrated RENAME TO user_context_items;`);
        this.db.run('COMMIT');
      } catch (err) {
        try {
          this.db.run('ROLLBACK');
        } catch {}
        throw err;
      }
    }
    this.db.run(`
      CREATE TABLE IF NOT EXISTS user_context_settings (
        id INTEGER PRIMARY KEY CHECK (id = 1),
        enabled INTEGER NOT NULL DEFAULT 1,
        auto_capture INTEGER NOT NULL DEFAULT 1,
        updated_at TEXT NOT NULL
      );
    `);
    this.db.run(
      `INSERT OR IGNORE INTO user_context_settings (id, enabled, auto_capture, updated_at) VALUES (1, 1, 1, ?)`,
      [new Date().toISOString()]
    );
  }

  private getSettings(): { enabled: boolean; autoCapture: boolean; updatedAt: string } {
    const row = this.db.query<any, []>(`SELECT enabled, auto_capture, updated_at FROM user_context_settings WHERE id = 1`).get();
    return {
      enabled: row?.enabled !== 0,
      autoCapture: row?.auto_capture !== 0,
      updatedAt: String(row?.updated_at || new Date().toISOString()),
    };
  }

  public getProfile(): UserContextProfile {
    const settings = this.getSettings();
    const rows = this.db.query<any, []>(`SELECT * FROM user_context_items ORDER BY updated_at DESC, created_at DESC`).all();
    const items = rows.map(rowToItem);
    return {
      schemaVersion: 1,
      enabled: settings.enabled,
      autoCapture: settings.autoCapture,
      updatedAt: items[0]?.updatedAt || settings.updatedAt,
      items,
    };
  }

  public setSettings(input: { enabled?: boolean; autoCapture?: boolean }): UserContextProfile {
    const current = this.getSettings();
    this.db.run(
      `UPDATE user_context_settings SET enabled = ?, auto_capture = ?, updated_at = ? WHERE id = 1`,
      [
        input.enabled === undefined ? Number(current.enabled) : Number(input.enabled),
        input.autoCapture === undefined ? Number(current.autoCapture) : Number(input.autoCapture),
        new Date().toISOString(),
      ]
    );
    return this.getProfile();
  }

  public captureExplicitDirective(userPrompt: string, sessionId?: string): UserContextItem | null {
    if (!this.getSettings().autoCapture) return null;
    const prompt = userPrompt.trim();
    if (prompt.length < 12 || prompt.length > 1500) return null;
    const match = prompt.match(/\b(?:remember(?: that| this)?|from now on|for all projects|always use|never use|i prefer(?: using)?|i like)\s*:?\s*(.{8,800})$/i);
    if (!match) return null;
    const value = match[1]!.trim().replace(/[\s.]+$/, '');
    if (value.length < 8 || isCredentialLike(value)) return null;
    const { category, key } = classifyDirective(value);
    return this.upsert({
      category,
      key,
      value,
      source: 'explicit',
      evidenceText: match[0]!.trim(),
      confidence: 0.98,
      sourceSessionId: sessionId || null,
    });
  }

  public addManual(input: { category: UserContextCategory; value: string }): UserContextItem {
    if (!USER_CONTEXT_CATEGORIES.has(input.category)) throw new Error('Invalid personal context category.');
    const value = input.value.trim().slice(0, 800);
    if (!value) throw new Error('Personal context value is required.');
    if (isCredentialLike(value)) throw new Error('Personal context cannot contain credentials or access tokens.');
    const id = `uctx-${crypto.randomUUID().slice(0, 12)}`;
    return this.upsert({
      id,
      category: input.category,
      key: `manual.${id}`,
      value,
      source: 'manual',
      evidenceText: null,
      confidence: 1,
      sourceSessionId: null,
    });
  }

  public rememberFromConversation(input: {
    category: UserContextCategory;
    key: string;
    value: string;
    evidence: string;
    sessionId?: string;
  }): UserContextItem {
    if (!this.getSettings().autoCapture) throw new Error('Automatic personal context capture is disabled.');
    if (!USER_CONTEXT_CATEGORIES.has(input.category)) throw new Error('Invalid personal context category.');
    const value = input.value.trim().slice(0, 800);
    const key = input.key.trim().toLowerCase().replace(/[^a-z0-9_.-]+/g, '_').slice(0, 100);
    if (!key || value.length < 4) throw new Error('A short key and a useful context value are required.');
    const evidence = input.evidence.trim();
    if (!evidence || evidence.length > 1000 || isCredentialLike(value) || isCredentialLike(evidence)) {
      throw new Error('Personal context requires a short user quote and cannot contain credentials or access tokens.');
    }
    return this.upsert({
      category: input.category,
      key,
      value,
      source: 'explicit',
      evidenceText: evidence,
      confidence: 0.98,
      sourceSessionId: input.sessionId || null,
    });
  }

  public updateItem(id: string, input: { category?: UserContextCategory; value?: string }): UserContextItem | null {
    const existing = this.db.query<any, [string]>(`SELECT * FROM user_context_items WHERE id = ?`).get(id);
    if (!existing) return null;
    const value = input.value === undefined ? String(existing.value) : input.value.trim().slice(0, 800);
    if (!value) throw new Error('Personal context value is required.');
    if (isCredentialLike(value)) throw new Error('Personal context cannot contain credentials or access tokens.');
    const now = new Date().toISOString();
    this.db.run(
      `UPDATE user_context_items SET category = ?, value = ?, source = 'manual', evidence_text = NULL,
       source_session_id = NULL, confidence = 1, updated_at = ?, last_confirmed_at = ? WHERE id = ?`,
      [input.category || existing.category, value, now, now, id]
    );
    return rowToItem(this.db.query<any, [string]>(`SELECT * FROM user_context_items WHERE id = ?`).get(id));
  }

  public deleteItem(id: string): boolean {
    return Number(this.db.run(`DELETE FROM user_context_items WHERE id = ?`, [id]).changes || 0) > 0;
  }

  public clear(): void {
    this.db.run(`DELETE FROM user_context_items`);
  }

  /** Scan saved user-authored text locally. Candidates require explicit user review before saving. */
  public scanPastChatsForCandidates(limit = 80): UserContextCandidate[] {
    const rows = this.db.query<any, [number]>(
      `SELECT session_id, title, messages_json FROM chat_sessions ORDER BY updated_at DESC LIMIT ?`
    ).all(Math.max(1, Math.min(1000, limit)));
    const patterns: Array<{ regex: RegExp; category: UserContextCategory; key: string }> = [
      { regex: /\b(?:i am|i'm|i currently work as|i work as|i work at|my current role is|my current job is|my job is)\s+[^.!?\n]{4,180}/i, category: 'career', key: 'career.current_role' },
      { regex: /\b(?:i want to become|i aspire to be|my career goal is|i plan to become|i hope to become)\s+[^.!?\n]{4,180}/i, category: 'career', key: 'career.aspirations' },
      { regex: /\b(?:my wife|my husband|my partner|my spouse|my family|my parents|my mother|my father|my children|my kids|my son|my daughter|i have \d+ children|i live with)\b[^.!?\n]{0,180}/i, category: 'family', key: 'family.context' },
      { regex: /\b(?:my salary|my income|i earn|i owe|my debt|my finances|my financial situation|my budget|my mortgage|my savings|my retirement|financial goal|household income)\b[^.!?\n]{0,180}/i, category: 'finances', key: 'finances.context' },
      { regex: /\b(?:my long[- ]term goal is|my goal is to|my aspiration is to|i am working toward|i'm working toward|i plan to|i want to|i hope to)\s+[^.!?\n]{4,180}/i, category: 'goals', key: 'goals.long_term' },
    ];
    const candidates = new Map<string, UserContextCandidate>();
    for (const row of rows) {
      let messages: any[] = [];
      try {
        const parsed = JSON.parse(String(row.messages_json || '[]'));
        if (Array.isArray(parsed)) messages = parsed;
      } catch {}
      for (const message of messages) {
        if (message?.role !== 'user' || typeof message.content !== 'string') continue;
        const sentences = message.content.match(/[^.!?\n]+[.!?]?/g) || [message.content];
        for (const sentenceRaw of sentences) {
          const sentence = sentenceRaw.trim();
          if (!sentence || sentence.length < 12) continue;
          for (const pattern of patterns) {
            const match = sentence.match(pattern.regex);
            if (!match) continue;
            const evidence = match[0].trim();
            if (isCredentialLike(evidence)) continue;
            const id = `${pattern.key}:${evidence.toLowerCase()}`;
            if (!candidates.has(id)) {
              const candidateKey = `${pattern.key}.${crypto.createHash('sha1').update(evidence.toLowerCase()).digest('hex').slice(0, 10)}`;
              const existing = this.db.query<any, [string, string]>(
                `SELECT value FROM user_context_items WHERE category = ? AND context_key = ?`
              ).get(pattern.category, candidateKey);
              if (existing?.value === evidence) continue;
              candidates.set(id, {
                category: pattern.category,
                key: candidateKey,
                value: evidence,
                evidence,
                sessionId: String(row.session_id),
                chatTitle: String(row.title || 'Saved chat'),
              });
            }
          }
        }
      }
    }
    return Array.from(candidates.values()).slice(0, 80);
  }

  public acceptPastChatCandidate(candidate: UserContextCandidate): UserContextItem {
    const row = this.db.query<any, [string]>(
      `SELECT messages_json FROM chat_sessions WHERE session_id = ?`
    ).get(candidate.sessionId);
    if (!row) throw new Error('The source chat for this memory could not be found.');
    let messages: any[] = [];
    try {
      const parsed = JSON.parse(String(row.messages_json || '[]'));
      if (Array.isArray(parsed)) messages = parsed;
    } catch {}
    const supported = messages.some(
      (message) => message?.role === 'user' && typeof message.content === 'string' && message.content.includes(candidate.evidence)
    );
    if (!supported) throw new Error('The memory evidence no longer matches the saved user message.');
    if (!USER_CONTEXT_CATEGORIES.has(candidate.category)) throw new Error('Invalid personal context category.');
    const value = candidate.value.trim().slice(0, 800);
    if (!value || isCredentialLike(value)) throw new Error('This memory cannot be saved.');
    return this.upsert({
      category: candidate.category,
      key: candidate.key,
      value,
      source: 'imported',
      evidenceText: candidate.evidence,
      confidence: 0.95,
      sourceSessionId: candidate.sessionId,
    });
  }

  public buildPromptBlock(maxChars = 2400): string {
    const profile = this.getProfile();
    if (!profile.enabled || profile.items.length === 0) return '';
    const lines: string[] = [];
    for (const item of profile.items) {
      const dateLabel = item.source === 'imported' ? 'imported from saved chat' : `last shared ${item.lastConfirmedAt.slice(0, 10)}`;
      const safeValue = item.value.replace(/[\u0000-\u001f\u007f]/g, ' ').replace(/</g, '&lt;').replace(/>/g, '&gt;');
      const line = `- [${item.category}; ${dateLabel}] ${safeValue}`;
      if (lines.join('\n').length + line.length > maxChars) continue;
      lines.push(line);
    }
    if (lines.length === 0) return '';
    return [
      '<USER_PERSONAL_CONTEXT>',
      'Use these user-provided preferences and facts to personalize your response. They may become outdated; verify time-sensitive job, family, financial, or health details before relying on them. The current request takes precedence; project facts must be verified from the project.',
      ...lines,
      '</USER_PERSONAL_CONTEXT>',
    ].join('\n');
  }

  private upsert(input: {
    id?: string;
    category: UserContextCategory;
    key: string;
    value: string;
    source: 'explicit' | 'manual' | 'imported';
    evidenceText: string | null;
    confidence: number;
    sourceSessionId: string | null;
  }): UserContextItem {
    const now = new Date().toISOString();
    const id = input.id || `uctx-${crypto.randomUUID().slice(0, 12)}`;
    this.db.run(
      `INSERT INTO user_context_items (id, category, context_key, value, source, confidence, source_session_id, evidence_text, created_at, updated_at, last_confirmed_at)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
       ON CONFLICT(category, context_key) DO UPDATE SET
         value = excluded.value,
         source = excluded.source,
         evidence_text = excluded.evidence_text,
         confidence = excluded.confidence,
         source_session_id = excluded.source_session_id,
         updated_at = excluded.updated_at,
         last_confirmed_at = excluded.last_confirmed_at`,
      [id, input.category, input.key, input.value, input.source, input.confidence, input.sourceSessionId, input.evidenceText, now, now, now]
    );
    return rowToItem(this.db.query<any, [string, string]>(`SELECT * FROM user_context_items WHERE category = ? AND context_key = ?`).get(input.category, input.key));
  }
}

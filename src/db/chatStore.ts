/**
 * SQLite-backed chat sessions, run checkpoints, and AHP sync state.
 */

import type { Database } from 'bun:sqlite';
import crypto from 'node:crypto';
import type { PlanNode } from '../core/contextEnvelope';
import type { PendingApproval, RunCheckpoint, RunVerificationRecord } from '../runtime/types';

function stableMessageId(message: unknown, index: number): string {
  if (message && typeof message === 'object' && typeof (message as any).id === 'string' && (message as any).id.trim()) {
    return String((message as any).id);
  }
  return `legacy-${crypto.createHash('sha256').update((JSON.stringify(message) || 'null') + `:${index}`).digest('hex').slice(0, 16)}`;
}

function messageTimestamp(message: any, fallbackOrder: number): number {
  if (message && typeof message.createdAt === 'string') {
    const parsed = Date.parse(message.createdAt);
    if (Number.isFinite(parsed)) return parsed;
  }
  const match = String(message?.id || '').match(/(?:^|-)\d{13}(?:-|$)/);
  if (match) return Number(match[0].replace(/-/g, ''));
  return Number.MAX_SAFE_INTEGER - 1_000_000 + fallbackOrder;
}

function canonicalJson(value: unknown): string {
  if (value === null || typeof value !== 'object') {
    return JSON.stringify(value);
  }
  if (Array.isArray(value)) {
    return `[${value.map((item) => (item === undefined ? 'null' : canonicalJson(item))).join(',')}]`;
  }
  const obj = value as Record<string, unknown>;
  const keys = Object.keys(obj)
    .filter((k) => obj[k] !== undefined)
    .sort();
  return `{${keys.map((k) => `${JSON.stringify(k)}:${canonicalJson(obj[k])}`).join(',')}}`;
}

function stripSyncFields(message: Record<string, unknown>): Record<string, unknown> {
  const copy = { ...message };
  delete copy.id;
  delete copy.syncConflictOf;
  delete copy.syncConflictSource;
  return copy;
}

/** Merge append-only chat messages from two device snapshots without dropping either side. */
function mergeChatMessages(localMessages: unknown[], remoteMessages: unknown[]): unknown[] {
  const merged = new Map<string, { message: any; order: number }>();
  const add = (raw: unknown, order: number, source: 'local' | 'remote') => {
    const message: Record<string, any> =
      raw && typeof raw === 'object'
        ? { ...(raw as Record<string, unknown>) }
        : { role: 'assistant', content: String(raw ?? '') };
    const id = stableMessageId(message, order);
    message.id = id;
    const existing = merged.get(id);
    if (!existing) {
      const conflictBase = typeof message.syncConflictOf === 'string' ? message.syncConflictOf : null;
      const baseMessage = conflictBase ? merged.get(conflictBase)?.message : undefined;
      if (
        baseMessage &&
        (canonicalJson(stripSyncFields(baseMessage)) === canonicalJson(stripSyncFields(message)) ||
          (String(baseMessage.role || '') === String(message.role || '') &&
            String(baseMessage.content ?? '').trim() === String(message.content ?? '').trim()))
      ) {
        return;
      }
      merged.set(id, { message, order });
      return;
    }
    if (canonicalJson(existing.message) === canonicalJson(message)) return;

    const sameRole = String(existing.message.role || '') === String(message.role || '');
    const sameText = String(existing.message.content ?? '').trim() === String(message.content ?? '').trim();
    if (sameRole && sameText) {
      // Content is identical (e.g. JSONB key reordering or metadata/steps update); merge metadata without forking.
      const existingSteps = Array.isArray(existing.message.steps) ? existing.message.steps.length : 0;
      const incomingSteps = Array.isArray(message.steps) ? message.steps.length : 0;
      merged.set(id, {
        message: {
          ...existing.message,
          ...message,
          id,
          steps: incomingSteps >= existingSteps ? message.steps : existing.message.steps,
        },
        order: existing.order,
      });
      return;
    }

    // Preserve a genuine text divergence as a visible second variant instead of choosing a writer silently.
    const variantHash = crypto.createHash('sha256').update(canonicalJson(stripSyncFields(message))).digest('hex').slice(0, 12);
    const variantId = `${id}~sync-${variantHash}`;
    if (!merged.has(variantId)) {
      message.id = variantId;
      message.syncConflictOf = id;
      message.syncConflictSource = source;
      merged.set(variantId, { message, order: order + 0.25 });
    }
  };

  localMessages.forEach((message, index) => add(message, index, 'local'));
  remoteMessages.forEach((message, index) => add(message, localMessages.length + index, 'remote'));
  return Array.from(merged.values())
    .sort((a, b) => messageTimestamp(a.message, a.order) - messageTimestamp(b.message, b.order) || a.order - b.order)
    .map((entry) => entry.message);
}

export class ChatStore {
  constructor(private readonly db: Database) {
    this.db.run(`
      CREATE TABLE IF NOT EXISTS chat_sync_conflicts (
        conflict_id TEXT PRIMARY KEY,
        session_id TEXT NOT NULL,
        field TEXT NOT NULL,
        local_value TEXT NOT NULL,
        remote_value TEXT NOT NULL,
        detected_at TEXT NOT NULL,
        resolved_at TEXT
      );
      CREATE INDEX IF NOT EXISTS idx_chat_sync_conflicts_open
      ON chat_sync_conflicts(resolved_at, detected_at);
    `);
  }

  private recordSyncConflict(sessionId: string, field: string, localValue: string, remoteValue: string): void {
    const id = crypto.createHash('sha256').update(`${sessionId}\0${field}\0${localValue}\0${remoteValue}`).digest('hex').slice(0, 24);
    this.db.run(
      `INSERT INTO chat_sync_conflicts
       (conflict_id, session_id, field, local_value, remote_value, detected_at, resolved_at)
       VALUES (?, ?, ?, ?, ?, ?, NULL)
       ON CONFLICT(conflict_id) DO UPDATE SET detected_at = excluded.detected_at, resolved_at = NULL`,
      [id, sessionId, field, localValue, remoteValue, new Date().toISOString()]
    );
  }

  public listUnresolvedSyncConflicts(): Array<{
    conflictId: string;
    sessionId: string;
    field: string;
    localValue: string;
    remoteValue: string;
    detectedAt: string;
  }> {
    return this.db.query<any, []>(
      `SELECT conflict_id, session_id, field, local_value, remote_value, detected_at
       FROM chat_sync_conflicts WHERE resolved_at IS NULL ORDER BY detected_at DESC LIMIT 100`
    ).all().map((row) => ({
      conflictId: String(row.conflict_id),
      sessionId: String(row.session_id),
      field: String(row.field),
      localValue: String(row.local_value),
      remoteValue: String(row.remote_value),
      detectedAt: String(row.detected_at),
    }));
  }

  public resolveSyncConflict(conflictId: string, choice: 'local' | 'remote'): boolean {
    const conflict = this.db.query<any, [string]>(
      `SELECT * FROM chat_sync_conflicts WHERE conflict_id = ? AND resolved_at IS NULL LIMIT 1`
    ).get(conflictId);
    if (!conflict) return false;
    const value = choice === 'remote' ? conflict.remote_value : conflict.local_value;
    const allowedFields: Record<string, string> = {
      title: 'title',
      project_id: 'project_id',
      project_label: 'project_label',
      model_id: 'model_id',
      mode: 'mode',
    };
    if (String(conflict.field) === 'deleted') {
      const now = new Date().toISOString();
      if (choice === 'remote') {
        this.db.run(`DELETE FROM chat_sessions WHERE session_id = ?`, [conflict.session_id]);
        this.db.run(`DELETE FROM run_checkpoints WHERE session_id = ?`, [conflict.session_id]);
      } else {
        this.db.run(
          `UPDATE chat_sessions SET dirty = 1, revision = revision + 1, updated_at = ? WHERE session_id = ?`,
          [now, conflict.session_id]
        );
      }
      this.db.run(`UPDATE chat_sync_conflicts SET resolved_at = ? WHERE conflict_id = ?`, [now, conflictId]);
      return true;
    }
    const column = allowedFields[String(conflict.field)];
    if (!column) return false;
    const now = new Date().toISOString();
    this.db.run(
      `UPDATE chat_sessions SET ${column} = ?, updated_at = ?, dirty = 1, revision = revision + 1 WHERE session_id = ?`,
      [value, now, conflict.session_id]
    );
    this.db.run(`UPDATE chat_sync_conflicts SET resolved_at = ? WHERE conflict_id = ?`, [now, conflictId]);
    return true;
  }

  public getLatestPinnedFacts(sessionId?: string): Record<string, string> {
    const row = sessionId
      ? this.db.query<any, [string]>(`SELECT checkpoint_json FROM run_checkpoints WHERE session_id = ? ORDER BY updated_at DESC LIMIT 1`).get(sessionId)
      : this.db.query<any, []>(`SELECT checkpoint_json FROM run_checkpoints ORDER BY updated_at DESC LIMIT 1`).get();
    if (!row) return {};
    try {
      const parsed = JSON.parse(row.checkpoint_json) as Partial<RunCheckpoint>;
      return parsed.pinnedFacts || {};
    } catch {
      return {};
    }
  }

  public saveChatSession(session: {
    sessionId: string;
    title: string;
    projectId?: string;
    projectLabel?: string;
    modelId?: string;
    mode?: 'chat' | 'code';
    messages: unknown[];
  }): void {
    const mode = session.mode === 'code' || (session.projectId && session.projectId !== 'local') ? 'code' : 'chat';
    this.db.run(
      `INSERT INTO chat_sessions (session_id, title, project_id, project_label, model_id, mode, messages_json, updated_at, dirty)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, 1)
       ON CONFLICT(session_id) DO UPDATE SET
         title = excluded.title,
         project_id = CASE WHEN ? = 1 THEN chat_sessions.project_id ELSE excluded.project_id END,
         project_label = CASE WHEN ? = 1 THEN chat_sessions.project_label ELSE excluded.project_label END,
         model_id = excluded.model_id,
         mode = excluded.mode,
         messages_json = excluded.messages_json,
         updated_at = excluded.updated_at,
         dirty = 1,
         revision = chat_sessions.revision + 1`,
      [
        session.sessionId,
        session.title,
        session.projectId || 'local',
        session.projectLabel || 'Local workspace',
        session.modelId || 'auto',
        mode,
        JSON.stringify(session.messages || []),
        new Date().toISOString(),
        session.projectId === undefined ? 1 : 0,
        session.projectLabel === undefined ? 1 : 0,
      ]
    );
  }

  public getChatSession(sessionId: string): {
    sessionId: string;
    title: string;
    projectId: string;
    projectLabel: string;
    modelId: string;
    mode: 'chat' | 'code';
    messages: unknown[];
    updatedAt: string;
    parentSessionId?: string;
    forkedFromMessageId?: string;
    forkedAtSequence?: number;
    baseCheckpointRunId?: string;
  } | null {
    const row = this.db.query<any, [string]>(
      `SELECT session_id, title, project_id, project_label, model_id, mode, messages_json, updated_at,
              parent_session_id, forked_from_message_id, forked_at_sequence, base_checkpoint_run_id
       FROM chat_sessions WHERE session_id = ? LIMIT 1`
    ).get(sessionId);
    if (!row) return null;
    let messages: unknown[] = [];
    try {
      messages = JSON.parse(row.messages_json);
    } catch {}
    return {
      sessionId: row.session_id,
      title: row.title,
      projectId: row.project_id,
      projectLabel: row.project_label,
      modelId: row.model_id,
      mode: row.mode === 'code' || row.project_id !== 'local' ? 'code' : 'chat',
      messages,
      updatedAt: row.updated_at,
      ...(row.parent_session_id ? { parentSessionId: String(row.parent_session_id) } : {}),
      ...(row.forked_from_message_id ? { forkedFromMessageId: String(row.forked_from_message_id) } : {}),
      ...(typeof row.forked_at_sequence === 'number' ? { forkedAtSequence: row.forked_at_sequence } : {}),
      ...(row.base_checkpoint_run_id ? { baseCheckpointRunId: String(row.base_checkpoint_run_id) } : {}),
    };
  }

  public forkChatSession(params: {
    sourceSessionId: string;
    newSessionId?: string;
    messageId?: string;
    targetModelId?: string;
    targetMode?: 'chat' | 'code';
    title?: string;
    baseCheckpointRunId?: string;
  }): ReturnType<ChatStore['getChatSession']> {
    const source = this.getChatSession(params.sourceSessionId);
    if (!source) return null;

    let slicedMessages = source.messages;
    if (params.messageId) {
      const idx = source.messages.findIndex(
        (m) => m && typeof m === 'object' && String((m as any).id || '') === params.messageId
      );
      if (idx >= 0) {
        slicedMessages = source.messages.slice(0, idx + 1);
      }
    }

    const newSessionId = params.newSessionId?.trim() || `fork-${Date.now()}-${crypto.randomUUID().slice(0, 8)}`;
    const nextTitle = params.title?.trim() || `${source.title} (Fork)`;
    const nextModelId = params.targetModelId?.trim() || source.modelId;
    const nextMode = params.targetMode || source.mode;
    const now = new Date().toISOString();
    const forkedAtSequence = slicedMessages.length;

    this.db.run(
      `INSERT INTO chat_sessions (
         session_id, title, project_id, project_label, model_id, mode, messages_json, updated_at,
         dirty, parent_session_id, forked_from_message_id, forked_at_sequence, base_checkpoint_run_id
       )
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?, ?, ?)`,
      [
        newSessionId,
        nextTitle,
        source.projectId,
        source.projectLabel,
        nextModelId,
        nextMode,
        JSON.stringify(slicedMessages),
        now,
        source.sessionId,
        params.messageId || null,
        forkedAtSequence,
        params.baseCheckpointRunId || null,
      ]
    );

    return this.getChatSession(newSessionId);
  }

  public purgeExpiredArchivedChats(retentionDays = 30): number {
    const cutoff = new Date(Date.now() - retentionDays * 24 * 60 * 60 * 1000).toISOString();
    const res = this.db.run(
      `DELETE FROM chat_sessions WHERE archived_at IS NOT NULL AND archived_at < ?`,
      [cutoff]
    );
    return Number(res.changes || 0);
  }

  public archiveChatSession(sessionId: string, archived: boolean): void {
    const archivedAt = archived ? new Date().toISOString() : null;
    this.db.run(
      `UPDATE chat_sessions
       SET archived_at = ?, dirty = 1, revision = revision + 1
       WHERE session_id = ?`,
      [archivedAt, sessionId]
    );
  }

  public listArchivedChatSessions(limit = 50, retentionDays = 30): Array<{
    sessionId: string;
    title: string;
    projectId: string;
    projectLabel: string;
    modelId: string;
    mode: 'chat' | 'code';
    updatedAt: string;
    archivedAt: string;
    daysRemaining: number;
  }> {
    this.purgeExpiredArchivedChats(retentionDays);
    const rows = this.db.query<any, [number]>(
      `SELECT session_id, title, project_id, project_label, model_id, mode, updated_at, archived_at
       FROM chat_sessions
       WHERE archived_at IS NOT NULL
       ORDER BY archived_at DESC LIMIT ?`
    ).all(limit);
    const now = Date.now();
    const retentionMs = retentionDays * 24 * 60 * 60 * 1000;
    return rows.map((r) => {
      const archivedMs = Date.parse(r.archived_at) || now;
      const daysRemaining = Math.max(1, Math.ceil((archivedMs + retentionMs - now) / (24 * 60 * 60 * 1000)));
      return {
        sessionId: r.session_id,
        title: r.title,
        projectId: r.project_id,
        projectLabel: r.project_label,
        modelId: r.model_id,
        mode: r.mode === 'code' || r.project_id !== 'local' ? 'code' : 'chat',
        updatedAt: r.updated_at,
        archivedAt: r.archived_at,
        daysRemaining,
      };
    });
  }

  public listChatSessions(limit = 50, mode?: 'chat' | 'code'): Array<{
    sessionId: string;
    title: string;
    projectId: string;
    projectLabel: string;
    modelId: string;
    mode: 'chat' | 'code';
    updatedAt: string;
    parentSessionId?: string;
    forkedFromMessageId?: string;
    forkedAtSequence?: number;
  }> {
    this.purgeExpiredArchivedChats(30);
    const rows = mode === 'code'
      ? this.db.query<any, [number]>(
          `SELECT session_id, title, project_id, project_label, model_id, mode, updated_at,
                  parent_session_id, forked_from_message_id, forked_at_sequence
           FROM chat_sessions
           WHERE (mode = 'code' OR project_id != 'local') AND archived_at IS NULL
           ORDER BY updated_at DESC LIMIT ?`
        ).all(limit)
      : mode === 'chat'
      ? this.db.query<any, [number]>(
          `SELECT session_id, title, project_id, project_label, model_id, mode, updated_at,
                  parent_session_id, forked_from_message_id, forked_at_sequence
           FROM chat_sessions
           WHERE mode = 'chat' AND project_id = 'local' AND archived_at IS NULL
           ORDER BY updated_at DESC LIMIT ?`
        ).all(limit)
      : this.db.query<any, [number]>(
          `SELECT session_id, title, project_id, project_label, model_id, mode, updated_at,
                  parent_session_id, forked_from_message_id, forked_at_sequence
           FROM chat_sessions
           WHERE archived_at IS NULL
           ORDER BY updated_at DESC LIMIT ?`
        ).all(limit);
    return rows.map((r) => ({
      sessionId: r.session_id,
      title: r.title,
      projectId: r.project_id,
      projectLabel: r.project_label,
      modelId: r.model_id,
      mode: r.mode === 'code' || r.project_id !== 'local' ? 'code' : 'chat',
      updatedAt: r.updated_at,
      ...(r.parent_session_id ? { parentSessionId: String(r.parent_session_id) } : {}),
      ...(r.forked_from_message_id ? { forkedFromMessageId: String(r.forked_from_message_id) } : {}),
      ...(typeof r.forked_at_sequence === 'number' ? { forkedAtSequence: r.forked_at_sequence } : {}),
    }));
  }

  public deleteChatSession(sessionId: string): void {
    this.db.run(`DELETE FROM chat_sessions WHERE session_id = ?`, [sessionId]);
    this.db.run(`DELETE FROM run_checkpoints WHERE session_id = ?`, [sessionId]);
    this.db.run(`DELETE FROM chat_sync_conflicts WHERE session_id = ?`, [sessionId]);
    const row = this.db.query<any, [string]>(`SELECT value FROM sync_state WHERE key = ?`).get('deleted_chat_ids');
    let existing: string[] = [];
    if (row) {
      try {
        existing = JSON.parse(row.value);
      } catch {}
    }
    if (!existing.includes(sessionId)) {
      existing.push(sessionId);
    }
    this.db.run(
      `INSERT INTO sync_state (key, value) VALUES ('deleted_chat_ids', ?)
       ON CONFLICT(key) DO UPDATE SET value = excluded.value`,
      [JSON.stringify(existing)]
    );
  }

  public clearAllHistory(): void {
    this.db.run(`DELETE FROM chat_sessions`);
    this.db.run(`DELETE FROM run_checkpoints`);
    this.db.run(`DELETE FROM chat_sync_conflicts`);
    this.db.run(`DELETE FROM sync_state`);
  }

  public getDirtySyncPayload(): {
    sinceSeq: number;
    chats: Array<{
      sessionId: string;
      title: string;
      projectId: string;
      projectLabel: string;
      modelId: string;
      messages: unknown[];
      updatedAt: string;
      revision: number;
      mode: 'chat' | 'code';
    }>;
    tasks: RunCheckpoint[];
    deletedChatIds: string[];
  } {
    const seqRow = this.db.query<any, [string]>(`SELECT value FROM sync_state WHERE key = ?`).get('last_pulled_seq');
    const sinceSeq = seqRow ? parseInt(seqRow.value, 10) || 0 : 0;

    const delRow = this.db.query<any, [string]>(`SELECT value FROM sync_state WHERE key = ?`).get('deleted_chat_ids');
    let deletedChatIds: string[] = [];
    if (delRow) {
      try {
        deletedChatIds = JSON.parse(delRow.value);
      } catch {}
    }

    const chatRows = this.db.query<any, []>(
      `SELECT session_id, title, project_id, project_label, model_id, mode, revision, messages_json, updated_at
       FROM chat_sessions WHERE dirty = 1 ORDER BY updated_at ASC`
    ).all();
    const chats = chatRows.map((r) => {
      let messages: unknown[] = [];
      try {
        messages = JSON.parse(r.messages_json);
      } catch {}
      return {
        sessionId: r.session_id,
        title: r.title,
        projectId: r.project_id,
        projectLabel: r.project_label,
        modelId: r.model_id,
        mode: r.mode === 'code' ? ('code' as const) : ('chat' as const),
        revision: Number(r.revision || 1),
        messages,
        updatedAt: r.updated_at,
      };
    });

    const taskRows = this.db.query<any, []>(
      `SELECT checkpoint_json FROM run_checkpoints WHERE dirty = 1 ORDER BY updated_at ASC`
    ).all();
    const tasks: RunCheckpoint[] = [];
    for (const r of taskRows) {
      try {
        tasks.push(JSON.parse(r.checkpoint_json));
      } catch {}
    }

    return { sinceSeq, chats, tasks, deletedChatIds };
  }

  public applyRemoteSyncDelta(delta: {
    latestSeq: number;
    chats?: Array<{
      sessionId: string;
      title: string;
      projectId?: string;
      projectLabel?: string;
      modelId?: string;
      mode?: 'chat' | 'code';
      messages?: unknown[];
      updatedAt?: string;
      syncSeq?: number;
      revision?: number;
    }>;
    tasks?: Array<RunCheckpoint & { syncSeq?: number }>;
    deletedChatIds?: string[];
    pushedChats?: Array<{ sessionId: string; revision: number }>;
    pushedChatIds?: string[];
    pushedRunKeys?: Array<{ sessionId: string; runId: string }>;
  }): void {
    if (Array.isArray(delta.pushedChats)) {
      for (const chat of delta.pushedChats) {
        this.db.run(
          `UPDATE chat_sessions SET dirty = 0, sync_seq = ? WHERE session_id = ? AND revision = ?`,
          [delta.latestSeq, chat.sessionId, chat.revision]
        );
      }
    } else if (Array.isArray(delta.pushedChatIds)) {
      for (const sessionId of delta.pushedChatIds) {
        this.db.run(`UPDATE chat_sessions SET dirty = 0, sync_seq = ? WHERE session_id = ?`, [
          delta.latestSeq,
          sessionId,
        ]);
      }
    }
    if (Array.isArray(delta.pushedRunKeys)) {
      for (const k of delta.pushedRunKeys) {
        this.db.run(
          `UPDATE run_checkpoints SET dirty = 0, sync_seq = ? WHERE session_id = ? AND run_id = ?`,
          [delta.latestSeq, k.sessionId, k.runId]
        );
      }
    }
    if (Array.isArray(delta.deletedChatIds)) {
      for (const id of delta.deletedChatIds) {
        const existing = this.db.query<any, [string]>(`SELECT dirty FROM chat_sessions WHERE session_id = ?`).get(id);
        if (existing?.dirty) {
          this.recordSyncConflict(id, 'deleted', 'local changes', 'deleted on another device');
          continue;
        }
        this.db.run(`DELETE FROM chat_sessions WHERE session_id = ?`, [id]);
        this.db.run(`DELETE FROM run_checkpoints WHERE session_id = ?`, [id]);
        this.db.run(`DELETE FROM chat_sync_conflicts WHERE session_id = ?`, [id]);
      }
    }
    if (Array.isArray(delta.chats)) {
      for (const chat of delta.chats) {
        const existing = this.db.query<any, [string]>(
          `SELECT title, project_id, project_label, model_id, mode, messages_json, updated_at, revision, dirty
           FROM chat_sessions WHERE session_id = ?`
        ).get(chat.sessionId);
        const incomingTime = chat.updatedAt || new Date().toISOString();
        let localMessages: unknown[] = [];
        try {
          if (existing?.messages_json) localMessages = JSON.parse(existing.messages_json);
        } catch {}
        const remoteMessages = Array.isArray(chat.messages) ? chat.messages : [];
        const mergedMessages = mergeChatMessages(localMessages, remoteMessages);
        const mergedJson = JSON.stringify(mergedMessages);
        const hasLocalOnlyMessages = mergedMessages.length > remoteMessages.length;

        if (existing?.dirty) {
          const remoteMetadata: Record<string, string> = {
            title: chat.title || 'Untitled chat',
            project_id: chat.projectId || 'local',
            project_label: chat.projectLabel || 'Local workspace',
            model_id: chat.modelId || 'auto',
            mode: chat.mode === 'code' ? 'code' : 'chat',
          };
          for (const [field, remoteValue] of Object.entries(remoteMetadata)) {
            const localValue = String(existing[field] ?? '');
            if (localValue !== remoteValue) this.recordSyncConflict(chat.sessionId, field, localValue, remoteValue);
          }
          if (mergedJson !== JSON.stringify(localMessages)) {
            this.db.run(
              `UPDATE chat_sessions
               SET messages_json = ?, revision = revision + 1, updated_at = ?, dirty = 1
               WHERE session_id = ?`,
              [mergedJson, new Date().toISOString(), chat.sessionId]
            );
          }
          continue;
        }

        const incomingIsNewer = !existing || incomingTime >= existing.updated_at || (chat.revision ?? 1) > Number(existing.revision || 1);
        if (incomingIsNewer || mergedJson !== JSON.stringify(localMessages)) {
          const nextRevision = Math.max(Number(existing?.revision || 0), Number(chat.revision || 1)) + (hasLocalOnlyMessages ? 1 : 0);
          this.db.run(
            `INSERT INTO chat_sessions (session_id, title, project_id, project_label, model_id, mode, revision, messages_json, updated_at, dirty, sync_seq)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
             ON CONFLICT(session_id) DO UPDATE SET
               title = excluded.title,
               project_id = excluded.project_id,
               project_label = excluded.project_label,
               model_id = excluded.model_id,
               mode = excluded.mode,
               revision = excluded.revision,
               messages_json = excluded.messages_json,
               updated_at = excluded.updated_at,
               dirty = excluded.dirty,
               sync_seq = excluded.sync_seq`,
            [
              chat.sessionId,
              incomingIsNewer ? (chat.title || 'Untitled chat') : existing?.title || 'Untitled chat',
              incomingIsNewer ? (chat.projectId || 'local') : existing?.project_id || 'local',
              incomingIsNewer ? (chat.projectLabel || 'Local workspace') : existing?.project_label || 'Local workspace',
              incomingIsNewer ? (chat.modelId || 'auto') : existing?.model_id || 'auto',
              incomingIsNewer ? (chat.mode === 'code' ? 'code' : 'chat') : existing?.mode || 'chat',
              nextRevision,
              mergedJson,
              incomingIsNewer ? incomingTime : new Date().toISOString(),
              Number(hasLocalOnlyMessages),
              chat.syncSeq ?? delta.latestSeq,
            ]
          );
        }
      }
    }
    if (Array.isArray(delta.tasks)) {
      for (const cp of delta.tasks) {
        if (!cp?.sessionId || !cp?.runId) continue;
        const existing = this.db.query<any, [string, string]>(
          `SELECT sequence, updated_at FROM run_checkpoints WHERE session_id = ? AND run_id = ?`
        ).get(cp.sessionId, cp.runId);
        const incomingTime = cp.timestamp || new Date().toISOString();
        if (!existing || (cp.sequence ?? 0) >= (existing.sequence ?? 0)) {
          this.db.run(
            `INSERT INTO run_checkpoints (session_id, run_id, sequence, stage, checkpoint_json, updated_at, dirty, sync_seq)
             VALUES (?, ?, ?, ?, ?, ?, 0, ?)
             ON CONFLICT(session_id, run_id) DO UPDATE SET
               sequence = excluded.sequence,
               stage = excluded.stage,
               checkpoint_json = excluded.checkpoint_json,
               updated_at = excluded.updated_at,
               dirty = 0,
               sync_seq = excluded.sync_seq`,
            [
              cp.sessionId,
              cp.runId,
              cp.sequence ?? 1,
              cp.stage || 'completed',
              JSON.stringify(cp),
              incomingTime,
              cp.syncSeq ?? delta.latestSeq,
            ]
          );
        }
      }
    }
    this.db.run(
      `INSERT INTO sync_state (key, value) VALUES ('deleted_chat_ids', '[]')
       ON CONFLICT(key) DO UPDATE SET value = '[]'`
    );
    if (typeof delta.latestSeq === 'number' && delta.latestSeq >= 0) {
      this.db.run(
        `INSERT INTO sync_state (key, value) VALUES ('last_pulled_seq', ?)
         ON CONFLICT(key) DO UPDATE SET value = excluded.value`,
        [String(delta.latestSeq)]
      );
    }
  }

  public saveCheckpoint(checkpoint: RunCheckpoint): void {
    this.db.run(
      `INSERT INTO run_checkpoints (session_id, run_id, sequence, stage, checkpoint_json, updated_at, dirty)
       VALUES (?, ?, ?, ?, ?, ?, 1)
       ON CONFLICT(session_id, run_id) DO UPDATE SET
         sequence = excluded.sequence,
         stage = excluded.stage,
         checkpoint_json = excluded.checkpoint_json,
         updated_at = excluded.updated_at,
         dirty = 1`,
      [
        checkpoint.sessionId,
        checkpoint.runId,
        checkpoint.sequence,
        checkpoint.stage,
        JSON.stringify(checkpoint),
        new Date().toISOString(),
      ]
    );
  }

  public getCheckpoint(sessionId: string, runId: string): RunCheckpoint | null {
    const row = this.db.query<any, [string, string]>(
      `SELECT checkpoint_json FROM run_checkpoints WHERE session_id = ? AND run_id = ? LIMIT 1`
    ).get(sessionId, runId);

    if (!row) return null;
    return JSON.parse(row.checkpoint_json) as RunCheckpoint;
  }

  public deleteCheckpoint(sessionId: string, runId: string): void {
    this.db.run(`DELETE FROM run_checkpoints WHERE session_id = ? AND run_id = ?`, [sessionId, runId]);
  }

  public listRecentTasks(
    limit = 20,
    isRunActive: (runId: string) => boolean
  ): Array<{
    sessionId: string;
    runId: string;
    stage: string;
    title: string;
    prompt?: string;
    output?: string;
    projectId?: string;
    projectLabel?: string;
    kind?: 'agent' | 'swarm';
    workers?: Array<{ id: string; name: string; status: string }>;
    pendingApproval?: PendingApproval;
    activePlan?: PlanNode[];
    completedTaskIds?: string[];
    modifiedFiles?: string[];
    lastVerification?: RunVerificationRecord;
    worktreePath?: string;
    isActive?: boolean;
    updatedAt: string;
  }> {
    const rows = this.db.query<any, [number]>(
      `SELECT session_id, run_id, stage, checkpoint_json, updated_at
       FROM run_checkpoints
       ORDER BY updated_at DESC LIMIT ?`
    ).all(limit);

    return rows.map((r) => {
      let parsed: Partial<RunCheckpoint> = {};
      try {
        parsed = JSON.parse(r.checkpoint_json) as Partial<RunCheckpoint>;
      } catch {}
      const title = parsed.activePlan?.[0]?.title || parsed.prompt || r.run_id;
      return {
        sessionId: r.session_id,
        runId: r.run_id,
        stage: r.stage,
        title,
        prompt: parsed.prompt,
        output: parsed.output,
        projectId: parsed.projectId,
        projectLabel: parsed.projectLabel,
        kind: parsed.kind || 'agent',
        workers: parsed.workers,
        pendingApproval: parsed.pendingApproval,
        activePlan: parsed.activePlan || [],
        completedTaskIds: parsed.completedTaskIds || [],
        modifiedFiles: parsed.modifiedFiles || [],
        lastVerification: parsed.lastVerification,
        worktreePath: parsed.worktreePath,
        isActive: isRunActive(r.run_id),
        updatedAt: r.updated_at,
      };
    });
  }
}

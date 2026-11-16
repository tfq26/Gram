/**
 * SQLite-backed chat sessions, run checkpoints, and AHP sync state.
 */

import type { Database } from 'bun:sqlite';
import type { PlanNode } from '../core/contextEnvelope';
import type { PendingApproval, RunCheckpoint, RunVerificationRecord } from '../runtime/types';

export class ChatStore {
  constructor(private readonly db: Database) {}

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
    const mode = session.mode === 'code' ? 'code' : 'chat';
    this.db.run(
      `INSERT INTO chat_sessions (session_id, title, project_id, project_label, model_id, mode, messages_json, updated_at, dirty)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, 1)
       ON CONFLICT(session_id) DO UPDATE SET
         title = excluded.title,
         project_id = excluded.project_id,
         project_label = excluded.project_label,
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
  } | null {
    const row = this.db.query<any, [string]>(
      `SELECT session_id, title, project_id, project_label, model_id, mode, messages_json, updated_at
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
      mode: row.mode === 'code' ? 'code' : 'chat',
      messages,
      updatedAt: row.updated_at,
    };
  }

  public listChatSessions(limit = 50, mode?: 'chat' | 'code'): Array<{
    sessionId: string;
    title: string;
    projectId: string;
    projectLabel: string;
    modelId: string;
    mode: 'chat' | 'code';
    updatedAt: string;
  }> {
    const rows = mode
      ? this.db.query<any, [string, number]>(
          `SELECT session_id, title, project_id, project_label, model_id, mode, updated_at
           FROM chat_sessions
           WHERE mode = ?
           ORDER BY updated_at DESC LIMIT ?`
        ).all(mode, limit)
      : this.db.query<any, [number]>(
          `SELECT session_id, title, project_id, project_label, model_id, mode, updated_at
           FROM chat_sessions
           ORDER BY updated_at DESC LIMIT ?`
        ).all(limit);
    return rows.map((r) => ({
      sessionId: r.session_id,
      title: r.title,
      projectId: r.project_id,
      projectLabel: r.project_label,
      modelId: r.model_id,
      mode: r.mode === 'code' ? 'code' : 'chat',
      updatedAt: r.updated_at,
    }));
  }

  public deleteChatSession(sessionId: string): void {
    this.db.run(`DELETE FROM chat_sessions WHERE session_id = ?`, [sessionId]);
    this.db.run(`DELETE FROM run_checkpoints WHERE session_id = ?`, [sessionId]);
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
        this.db.run(`DELETE FROM chat_sessions WHERE session_id = ?`, [id]);
        this.db.run(`DELETE FROM run_checkpoints WHERE session_id = ?`, [id]);
      }
    }
    if (Array.isArray(delta.chats)) {
      for (const chat of delta.chats) {
        const existing = this.db.query<any, [string]>(
          `SELECT updated_at, revision, dirty FROM chat_sessions WHERE session_id = ?`
        ).get(chat.sessionId);
        const incomingTime = chat.updatedAt || new Date().toISOString();
        if ((!existing || incomingTime >= existing.updated_at) && !existing?.dirty) {
          this.db.run(
            `INSERT INTO chat_sessions (session_id, title, project_id, project_label, model_id, mode, revision, messages_json, updated_at, dirty, sync_seq)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 0, ?)
             ON CONFLICT(session_id) DO UPDATE SET
               title = excluded.title,
               project_id = excluded.project_id,
               project_label = excluded.project_label,
               model_id = excluded.model_id,
               mode = excluded.mode,
               revision = excluded.revision,
               messages_json = excluded.messages_json,
               updated_at = excluded.updated_at,
               dirty = 0,
               sync_seq = excluded.sync_seq`,
            [
              chat.sessionId,
              chat.title || 'Untitled chat',
              chat.projectId || 'local',
              chat.projectLabel || 'Local workspace',
              chat.modelId || 'auto',
              chat.mode === 'code' ? 'code' : 'chat',
              chat.revision ?? 1,
              JSON.stringify(chat.messages || []),
              incomingTime,
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

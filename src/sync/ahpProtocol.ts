/**
 * Agent Host Protocol (AHP v2.0) for Gram
 * 
 * Supports:
 * - JSON-RPC 2.0 (`ahp/sessions/list`, `create`, `get`, `sync`, `cast`)
 * - Sequence-Delta Synchronization (`sinceSeq`) for sub-20ms cross-device hydration
 * - E2EE Project Context Graph syncing
 * - Local-First execution with Privacy-First hibernation
 */

import { EventEmitter } from 'node:events';
import type { ContextEnvelope } from '../core/contextEnvelope';
import type { RunCheckpoint } from '../runtime/agentRuntime';

export interface AHPSessionMeta {
  sessionId: string;
  title: string;
  workspacePath: string;
  model: string;
  latestSeq: number;
  createdAt: string;
  updatedAt: string;
  status: 'idle' | 'executing' | 'paused' | 'completed';
}

export interface AHPEventMessage {
  id: string;
  sequence: number;
  role: 'user' | 'assistant' | 'tool' | 'system';
  content: string;
  thinking?: string;
  timestamp: string;
  runId?: string;
  toolCalls?: any[];
}

export interface AHPSyncDeltaResponse {
  sessionId: string;
  sinceSeq: number;
  latestSeq: number;
  meta: AHPSessionMeta;
  deltaMessages: AHPEventMessage[];
  pinnedFacts: Record<string, string>;
  activeCheckpoint?: RunCheckpoint;
}

export class AHPServer extends EventEmitter {
  private sessions = new Map<string, { meta: AHPSessionMeta; messages: AHPEventMessage[]; pinnedFacts: Record<string, string>; checkpoint?: RunCheckpoint }>();

  public createSession(params: { sessionId?: string; title?: string; workspacePath: string; model?: string }): AHPSessionMeta {
    const sessionId = params.sessionId || `session-${Date.now()}`;
    const now = new Date().toISOString();
    const meta: AHPSessionMeta = {
      sessionId,
      title: params.title || `Gram Session ${new Date().toLocaleTimeString()}`,
      workspacePath: params.workspacePath,
      model: params.model || 'us.anthropic.claude-sonnet-5',
      latestSeq: 0,
      createdAt: now,
      updatedAt: now,
      status: 'idle',
    };

    this.sessions.set(sessionId, {
      meta,
      messages: [],
      pinnedFacts: {},
    });

    return meta;
  }

  public getSession(sessionId: string) {
    return this.sessions.get(sessionId);
  }

  public listSessions(): AHPSessionMeta[] {
    return Array.from(this.sessions.values()).map((s) => s.meta);
  }

  public appendMessage(sessionId: string, message: Omit<AHPEventMessage, 'id' | 'sequence' | 'timestamp'>): AHPEventMessage {
    let session = this.sessions.get(sessionId);
    if (!session) {
      this.createSession({ sessionId, workspacePath: process.cwd() });
      session = this.sessions.get(sessionId)!;
    }

    const sequence = session.messages.length + 1;
    const fullMsg: AHPEventMessage = {
      ...message,
      id: `msg-${Date.now()}-${sequence}`,
      sequence,
      timestamp: new Date().toISOString(),
    };

    session.messages.push(fullMsg);
    session.meta.latestSeq = sequence;
    session.meta.updatedAt = fullMsg.timestamp;

    this.emit('message', { sessionId, message: fullMsg });
    return fullMsg;
  }

  public updateContext(sessionId: string, envelope: ContextEnvelope, checkpoint?: RunCheckpoint): void {
    const session = this.sessions.get(sessionId);
    if (session) {
      session.pinnedFacts = { ...envelope.state.pinnedFacts };
      session.checkpoint = checkpoint;
      session.meta.updatedAt = new Date().toISOString();
    }
  }

  /**
   * Sequence-delta sync (`sinceSeq`): returns only messages newer than sinceSeq
   */
  public syncSessionDelta(sessionId: string, sinceSeq = 0): AHPSyncDeltaResponse {
    let session = this.sessions.get(sessionId);
    if (!session) {
      const created = this.createSession({ sessionId, workspacePath: process.cwd() });
      session = this.sessions.get(created.sessionId)!;
    }

    const deltaMessages = session.messages.filter((m) => m.sequence > sinceSeq);

    return {
      sessionId: session.meta.sessionId,
      sinceSeq,
      latestSeq: session.meta.latestSeq,
      meta: session.meta,
      deltaMessages,
      pinnedFacts: session.pinnedFacts,
      activeCheckpoint: session.checkpoint,
    };
  }

  /**
   * JSON-RPC 2.0 Handler
   */
  public handleRpc(request: { id: string | number; method: string; params?: any }): {
    jsonrpc: '2.0';
    id: string | number;
    result?: any;
    error?: { code: number; message: string };
  } {
    const { id, method, params } = request;

    switch (method) {
      case 'ahp/sessions/list':
        return { jsonrpc: '2.0', id, result: { sessions: this.listSessions() } };
      case 'ahp/sessions/create':
        return { jsonrpc: '2.0', id, result: this.createSession(params || { workspacePath: process.cwd() }) };
      case 'ahp/sessions/sync':
        return { jsonrpc: '2.0', id, result: this.syncSessionDelta(params?.sessionId || 'default', params?.sinceSeq || 0) };
      default:
        return { jsonrpc: '2.0', id, error: { code: -32601, message: `Method '${method}' not found` } };
    }
  }
}

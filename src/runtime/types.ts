/**
 * Shared types for GramAgentRuntime runs, checkpoints, and streaming events.
 */

import type { PlanNode } from '../core/contextEnvelope';
import type { ToolApprovalAction } from './agentTools';

export interface PendingApproval {
  id: string;
  title: string;
  command?: string;
  description?: string;
  workspaceRoot?: string;
  action?: ToolApprovalAction;
}

export interface RunVerificationRecord {
  command: string;
  passed: boolean;
  summary: string;
  timestamp: string;
}

export interface RunCheckpoint {
  sessionId: string;
  runId: string;
  sequence: number;
  stage: 'idle' | 'executing' | 'paused' | 'completed' | 'failed';
  prompt?: string;
  goal?: string;
  output?: string;
  projectId?: string;
  projectLabel?: string;
  workspaceRoot?: string;
  worktreePath?: string;
  modelId?: string;
  mode?: 'chat' | 'plan' | 'code';
  kind?: 'agent' | 'swarm';
  workers?: Array<{ id: string; name: string; status: string }>;
  pendingApproval?: PendingApproval;
  activePlan: PlanNode[];
  completedTaskIds: string[];
  completedWork?: string[];
  openQuestions?: string[];
  modifiedFiles?: string[];
  lastVerification?: RunVerificationRecord;
  nextAction?: string;
  pinnedFacts: Record<string, string>;
  activeLayerId: string;
  timestamp: string;
  toolCalls?: Array<{ id: string; name: string; command: string; result?: string; error?: string }>;
}

export type RuntimeStreamEvent =
  | { type: 'start'; runId: string; modelId: string }
  | { type: 'task_started'; runId: string; title: string }
  | { type: 'thinking_delta'; delta: string }
  | { type: 'text_delta'; delta: string }
  | { type: 'tool_start'; id: string; name: string; command: string }
  | { type: 'tool_result'; id: string; name: string; command: string; result?: string; error?: string }
  | { type: 'approval_required'; approval: PendingApproval; runId: string; sessionId: string }
  | { type: 'done'; result: any }
  | { type: 'error'; error: string };

export interface AgentRunOptions {
  sessionId: string;
  runId?: string;
  prompt: string;
  modelId?: string;
  plan?: PlanNode[];
  history?: Array<{ role: 'user' | 'assistant'; content: string }>;
  systemPrompt?: string;
  effort?: 'low' | 'medium' | 'high';
  mode?: 'chat' | 'plan' | 'code';
  isAsync?: boolean;
  projectId?: string;
  projectLabel?: string;
  workspaceRoot?: string;
  kind?: 'agent' | 'swarm';
  requireApproval?: boolean;
  isolateWorktree?: boolean;
  signal?: AbortSignal;
  lyra?: {
    escalate: (payload: Record<string, unknown>) => Promise<unknown>;
    status: (runId: string, sessionId?: string) => Promise<unknown>;
  };
  onChunk?: (delta: string) => void;
  onProgress?: (status: string) => void;
  onEvent?: (event: RuntimeStreamEvent) => void;
}

/**
 * Shared TypeScript domain interfaces for the Gram Studio React UI.
 */

export interface ModelItem {
  id: string;
  name: string;
  provider: string;
  source?: 'byok' | 'openrouter' | 'builtin';
  contributorWarning?: string;
}

export interface ChatTab {
  id: string;
  title: string;
  sessionId: string;
  modelId: string;
  mode?: 'chat' | 'plan' | 'code';
  projectId?: string;
}

export type TraceStep =
  | { type: 'reasoning'; text: string; state: 'streaming' | 'done'; durationSeconds?: number }
  | { type: 'tool-runCommand'; command: string; state: 'running' | 'output-available' | 'output-error'; output?: string; errorText?: string };

export interface ReplyReference {
  id: string;
  role: 'user' | 'assistant';
  content: string;
}

export interface TaskStepItem {
  title: string;
  status: 'done' | 'running' | 'error';
}

export interface TaskItem {
  id: string;
  title: string;
  steps: TaskStepItem[];
}

export interface ApprovalItem {
  id: string;
  runId: string;
  sessionId: string;
  command: string;
  description?: string;
  state: 'approval-requested' | 'approval-responded';
  approved?: boolean;
}

export interface ChatSessionSummary {
  sessionId: string;
  title: string;
  projectId: string;
  projectLabel: string;
  modelId: string;
  mode?: 'chat' | 'code';
  updatedAt: string;
}

export interface ChatEntry {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  runId?: string;
  remoteSessionId?: string;
  lane?: string;
  reviewLayerId?: string;
  replyTo?: ReplyReference;
  steps?: TraceStep[];
  tasks?: TaskItem[];
  approval?: ApprovalItem;
  workers?: Array<{ id?: string; name?: string; status?: string }>;
  previewUrl?: string;
  originalPrompt?: string;
  codingHandoffPrompt?: string;
  isError?: boolean;
  syncConflictOf?: string;
  syncConflictSource?: 'local' | 'remote';
  diagnostics?: Record<string, unknown>;
}

export interface ManagedProcessItem {
  id: string;
  label: string;
  command: string;
  cwd: string;
  pid: number;
  status: 'running' | 'exited' | 'stopped';
  exitCode: number | null;
  startedAt: string;
  stoppedAt?: string;
}

export interface ProjectFileItem {
  fileId: string;
  filename: string;
  mimeType?: string;
  sizeBytes: number;
  createdAt: string;
}

export interface ProjectItem {
  id: string;
  label: string;
  workspacePath?: string;
  instructions?: string;
  archived?: boolean;
  files?: ProjectFileItem[];
}

export interface DeviceItem {
  deviceId: string;
  deviceName: string;
  deviceType: string;
  kind?: 'local' | 'cloud' | 'network';
  workspacePath?: string;
  isExecutionHost?: boolean;
}

export interface AuthState {
  connected: boolean;
  authenticated: boolean;
  user: { id: string; name: string; email: string } | null;
}

import React, { useEffect, useRef, useState } from 'react';
import {
  ArrowUpIcon,
  ExternalLinkIcon,
  GripHorizontalIcon,
  Maximize2Icon,
  Minimize2Icon,
  RefreshCwIcon,
  SquareIcon,
  XIcon,
} from 'lucide-react';
import { PlanArtifactCard } from './PlanArtifactCard';
import { FileTree, type FileTreeNodeData } from './FileTree';
import type { DockSurface } from './WorkspaceDock';

export interface MiniChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
}

export interface ManagedProcessItem {
  processId: string;
  label: string;
  command: string;
  status: string;
  pid?: number;
  urls?: string[];
  recentLogs?: string[];
}

const STAGE_TABS: Array<{ id: Exclude<DockSurface, 'agents'>; label: string }> = [
  { id: 'files', label: 'Files' },
  { id: 'plan', label: 'Plan' },
  { id: 'diff', label: 'Diff' },
  { id: 'terminal', label: 'Terminal' },
  { id: 'preview', label: 'Preview' },
  { id: 'side-question', label: 'Scratchpad' },
];

export function ContentStage({
  surface,
  onSelectSurface,
  onClose,
  activeProjectId,
  planMarkdown,
  onPlanChange,
  onSavePlan,
  onApproveAndBuild,
  previewUrl,
  onPreviewUrlChange,
  managedProcesses,
  onRefreshProcesses,
  onStopProcess,
  onOpenExternal,
}: {
  surface: DockSurface;
  onSelectSurface?: (surface: DockSurface) => void;
  onClose: () => void;
  activeProjectId: string;
  planMarkdown: string;
  onPlanChange: (next: string) => void;
  onSavePlan: (markdown: string) => Promise<void> | void;
  onApproveAndBuild: (opts: { markdown: string; isolateWorktree: boolean }) => void;
  previewUrl: string;
  onPreviewUrlChange: (url: string) => void;
  managedProcesses: ManagedProcessItem[];
  onRefreshProcesses: () => void;
  onStopProcess: (processId: string) => Promise<void> | void;
  onOpenExternal: (url: string) => void;
}) {
  const [diffData, setDiffData] = useState<{ status: string; gitDiff: string; workspacePath: string } | null>(null);
  const [diffLoading, setDiffLoading] = useState(false);
  const [iframeKey, setIframeKey] = useState(0);
  const [sidePrompt, setSidePrompt] = useState('');
  const [sideMessages, setSideMessages] = useState<Array<{ role: 'user' | 'assistant'; text: string }>>([]);
  const [sideLoading, setSideLoading] = useState(false);
  const [diffError, setDiffError] = useState<string | null>(null);
  const [sideError, setSideError] = useState<string | null>(null);
  const [fileTree, setFileTree] = useState<FileTreeNodeData[]>([]);
  const [selectedFilePath, setSelectedFilePath] = useState<string>('');
  const [selectedFileContent, setSelectedFileContent] = useState<string>('');
  const [filesLoading, setFilesLoading] = useState(false);

  async function loadFileTree() {
    setFilesLoading(true);
    try {
      const res = await fetch('/api/files/tree');
      if (!res.ok) return;
      const data = await res.json();
      setFileTree(Array.isArray(data) ? data : []);
    } catch {
    } finally {
      setFilesLoading(false);
    }
  }

  async function openFile(filePath: string) {
    setSelectedFilePath(filePath);
    try {
      const res = await fetch(`/api/files/read?path=${encodeURIComponent(filePath)}`);
      if (!res.ok) return;
      const data = await res.json();
      setSelectedFileContent(typeof data?.content === 'string' ? data.content : '');
    } catch {}
  }

  const detectedUrls = Array.from(
    new Set(managedProcesses.flatMap((p) => p.urls || []))
  );

  async function loadDiff() {
    setDiffLoading(true);
    setDiffError(null);
    try {
      const res = await fetch(`/api/workspace/diff?projectId=${encodeURIComponent(activeProjectId)}`);
      const d = await res.json().catch(() => null);
      if (!res.ok) {
        throw new Error(d?.error || `Failed to load diff (${res.status}).`);
      }
      setDiffData({ status: d?.status || '', gitDiff: d?.gitDiff || '', workspacePath: d?.workspacePath || '' });
    } catch (err: any) {
      setDiffError(err?.message || 'Could not load workspace changes.');
    } finally {
      setDiffLoading(false);
    }
  }

  useEffect(() => {
    if (surface === 'files') void loadFileTree();
    if (surface === 'diff') void loadDiff();
    if (surface === 'terminal') onRefreshProcesses();
  }, [surface, activeProjectId]);

  async function sendSideQuestion(e: React.FormEvent) {
    e.preventDefault();
    const q = sidePrompt.trim();
    if (!q || sideLoading) return;
    setSidePrompt('');
    setSideError(null);
    setSideMessages((prev) => [...prev, { role: 'user', text: q }, { role: 'assistant', text: '' }]);
    setSideLoading(true);
    try {
      const res = await fetch('/api/run/stream', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          sessionId: `side-${Date.now()}`,
          prompt: q,
          mode: 'chat',
          projectId: activeProjectId,
        }),
      });
      if (!res.ok) {
        const errData = await res.json().catch(() => null);
        throw new Error(errData?.error || `Request failed (${res.status}).`);
      }
      const reader = res.body?.getReader();
      if (!reader) throw new Error('Response stream unavailable.');
      const decoder = new TextDecoder();
      let buffer = '';
      let acc = '';
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split('\n');
        buffer = lines.pop() || '';
        for (const line of lines) {
          if (!line.startsWith('data: ')) continue;
          try {
            const evt = JSON.parse(line.slice(6));
            if (evt.type === 'text_delta' && evt.delta) {
              acc += evt.delta;
              setSideMessages((prev) => {
                const next = [...prev];
                next[next.length - 1] = { role: 'assistant', text: acc };
                return next;
              });
            } else if (evt.type === 'error') {
              throw new Error(evt.error || 'Side question failed.');
            }
          } catch (parseErr: any) {
            if (parseErr instanceof Error && parseErr.message !== 'Unexpected end of JSON input') {
              if (!line.slice(6).startsWith('{')) continue;
              throw parseErr;
            }
          }
        }
      }
    } catch (err: any) {
      const msg = err?.message || 'Could not complete side question.';
      setSideError(msg);
      setSideMessages((prev) => {
        const next = [...prev];
        if (next.length > 0 && next[next.length - 1]?.role === 'assistant' && !next[next.length - 1]?.text) {
          next[next.length - 1] = { role: 'assistant', text: `Error: ${msg}` };
        }
        return next;
      });
    } finally {
      setSideLoading(false);
    }
  }

  return (
    <div className="flex h-full w-full flex-col overflow-hidden bg-[var(--bg-body)]">
      <div className="flex h-10 shrink-0 items-center justify-between gap-2 border-b border-[var(--border-color)] bg-[var(--bg-panel)] px-3">
        <div className="flex min-w-0 items-center gap-0.5 overflow-x-auto">
          {STAGE_TABS.map((tab) => {
            const active = surface === tab.id;
            return (
              <button
                key={tab.id}
                type="button"
                onClick={() => onSelectSurface?.(tab.id)}
                className={`shrink-0 rounded-md px-2.5 py-1 text-[11.5px] font-medium transition-colors cursor-pointer ${
                  active
                    ? 'bg-[var(--bg-active)] text-[var(--text-primary)]'
                    : 'text-[var(--text-muted)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)]'
                }`}
              >
                {tab.label}
              </button>
            );
          })}
        </div>
        <button
          type="button"
          onClick={onClose}
          title="Close stage"
          aria-label="Close stage"
          className="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-md text-[var(--text-muted)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
        >
          <XIcon size={13} />
        </button>
      </div>

      <div className="flex-1 overflow-auto p-4">
        {surface === 'files' && (
          <div className="grid h-full grid-cols-[200px_1fr] gap-3 overflow-hidden">
            <div className="flex flex-col overflow-y-auto rounded-lg border border-[var(--border-color)] bg-[var(--bg-panel)] p-2">
              <div className="mb-1.5 flex items-center justify-between px-1">
                <span className="text-[10px] font-medium uppercase tracking-[0.06em] text-[var(--text-muted)]">
                  Workspace
                </span>
                <button
                  type="button"
                  onClick={() => void loadFileTree()}
                  className="text-[10px] text-[var(--text-muted)] hover:text-[var(--text-primary)] cursor-pointer"
                >
                  {filesLoading ? '...' : 'Refresh'}
                </button>
              </div>
              <FileTree
                data={fileTree}
                selectedPath={selectedFilePath}
                onSelect={(p) => void openFile(p)}
              />
            </div>
            <div className="flex flex-col overflow-hidden rounded-lg border border-[var(--border-color)] bg-[var(--bg-panel)]">
              <div className="border-b border-[var(--border-color)] px-3 py-1.5 font-mono text-[11px] text-[var(--text-secondary)] truncate">
                {selectedFilePath || 'Select a file to inspect'}
              </div>
              <pre className="flex-1 overflow-auto p-3 font-mono text-[11.5px] leading-relaxed text-[var(--text-primary)]">
                {selectedFilePath ? selectedFileContent : 'Click any file in the tree to preview its contents.'}
              </pre>
            </div>
          </div>
        )}

        {surface === 'plan' && (
          <PlanArtifactCard
            variant="stage"
            markdown={planMarkdown}
            onChange={onPlanChange}
            onSave={onSavePlan}
            onApproveAndBuild={onApproveAndBuild}
          />
        )}

        {surface === 'preview' && (
          <div className="flex h-full flex-col gap-3">
            <div className="flex flex-wrap items-center gap-2">
              <input
                type="url"
                value={previewUrl}
                onChange={(e) => onPreviewUrlChange(e.target.value)}
                placeholder="http://localhost:5173"
                className="h-8 min-w-[240px] flex-1 rounded-lg border border-[var(--border-color)] bg-[var(--bg-panel)] px-3 text-xs text-[var(--text-primary)] outline-none"
              />
              {detectedUrls.map((u) => (
                <button
                  key={u}
                  type="button"
                  onClick={() => onPreviewUrlChange(u)}
                  className="rounded-lg border border-[var(--border-color)] bg-[var(--bg-panel)] px-2.5 py-1 text-[11px] text-[var(--text-secondary)] hover:text-[var(--text-primary)] cursor-pointer"
                >
                  {u}
                </button>
              ))}
              <button
                type="button"
                onClick={() => setIframeKey((k) => k + 1)}
                className="inline-flex h-8 items-center gap-1 rounded-lg border border-[var(--border-color)] px-2.5 text-xs text-[var(--text-secondary)] hover:bg-[var(--bg-hover)] cursor-pointer"
              >
                <RefreshCwIcon size={12} />
                <span>Reload</span>
              </button>
              <button
                type="button"
                onClick={() => onOpenExternal(previewUrl)}
                className="inline-flex h-8 items-center gap-1 rounded-lg border border-[var(--border-color)] px-2.5 text-xs text-[var(--text-secondary)] hover:bg-[var(--bg-hover)] cursor-pointer"
              >
                <ExternalLinkIcon size={12} />
                <span>Browser</span>
              </button>
            </div>
            <iframe
              key={`${previewUrl}-${iframeKey}`}
              src={previewUrl}
              title="Live Preview"
              className="w-full flex-1 rounded-xl border border-[var(--border-color)] bg-white"
            />
          </div>
        )}

        {surface === 'diff' && (
          <div className="mx-auto flex max-w-4xl flex-col gap-3">
            <div className="flex items-center justify-between">
              <span className="text-xs text-[var(--text-muted)]">{diffData?.workspacePath || 'Workspace'}</span>
              <button
                type="button"
                onClick={() => void loadDiff()}
                className="inline-flex items-center gap-1 rounded-lg border border-[var(--border-color)] px-2.5 py-1 text-xs text-[var(--text-secondary)] hover:bg-[var(--bg-hover)] cursor-pointer"
              >
                <RefreshCwIcon size={12} className={diffLoading ? 'animate-spin' : ''} />
                <span>Refresh</span>
              </button>
            </div>
            {diffError && (
              <div
                role="alert"
                className="flex items-center justify-between gap-2 rounded-xl border border-red-500/30 bg-red-500/10 px-3.5 py-2.5 text-xs text-red-200"
              >
                <span>{diffError}</span>
                <button
                  type="button"
                  onClick={() => void loadDiff()}
                  className="shrink-0 rounded-md border border-red-400/30 px-2 py-0.5 text-[11px] font-medium text-red-200 hover:bg-red-500/20 cursor-pointer"
                >
                  Retry
                </button>
              </div>
            )}
            {diffData?.status && (
              <pre className="rounded-xl border border-[var(--border-color)] bg-[var(--bg-panel)] p-3 font-mono text-xs text-[var(--text-primary)] overflow-x-auto">
                {diffData.status}
              </pre>
            )}
            <pre className="rounded-xl border border-[var(--border-color)] bg-[var(--bg-panel)] p-4 font-mono text-xs leading-relaxed text-[var(--text-secondary)] overflow-x-auto">
              {diffData?.gitDiff || 'Working tree clean — no unstaged git diff.'}
            </pre>
          </div>
        )}

        {surface === 'terminal' && (
          <div className="mx-auto flex max-w-4xl flex-col gap-3">
            <div className="flex items-center justify-between">
              <span className="text-xs text-[var(--text-muted)]">
                {managedProcesses.length} managed process{managedProcesses.length === 1 ? '' : 'es'}
              </span>
              <button
                type="button"
                onClick={onRefreshProcesses}
                className="inline-flex items-center gap-1 rounded-lg border border-[var(--border-color)] px-2.5 py-1 text-xs text-[var(--text-secondary)] hover:bg-[var(--bg-hover)] cursor-pointer"
              >
                <RefreshCwIcon size={12} />
                <span>Refresh</span>
              </button>
            </div>
            {managedProcesses.length === 0 ? (
              <div className="rounded-xl border border-[var(--border-color)] bg-[var(--bg-panel)] p-6 text-center text-xs text-[var(--text-muted)]">
                No managed background processes running yet.
              </div>
            ) : (
              managedProcesses.map((proc) => (
                <div
                  key={proc.processId}
                  className="flex flex-col gap-2 rounded-xl border border-[var(--border-color)] bg-[var(--bg-panel)] p-4"
                >
                  <div className="flex items-center justify-between gap-2">
                    <div className="min-w-0">
                      <div className="truncate text-xs font-semibold text-[var(--text-primary)]">{proc.label}</div>
                      <div className="truncate font-mono text-[11px] text-[var(--text-muted)]">{proc.command}</div>
                    </div>
                    <button
                      type="button"
                      onClick={() => void onStopProcess(proc.processId)}
                      className="inline-flex items-center gap-1 rounded-lg border border-[var(--border-color)] px-2.5 py-1 text-xs text-[var(--text-secondary)] hover:bg-[var(--bg-hover)] cursor-pointer"
                    >
                      <SquareIcon size={10} fill="currentColor" />
                      <span>Stop</span>
                    </button>
                  </div>
                  {proc.recentLogs && proc.recentLogs.length > 0 && (
                    <pre className="max-h-48 overflow-auto rounded-lg bg-[var(--bg-body)] p-2.5 font-mono text-[11px] text-[var(--text-secondary)]">
                      {proc.recentLogs.slice(-30).join('\n')}
                    </pre>
                  )}
                </div>
              ))
            )}
          </div>
        )}

        {surface === 'side-question' && (
          <div className="mx-auto flex h-full max-w-2xl flex-col gap-3">
            <div className="flex-1 overflow-y-auto rounded-xl border border-[var(--border-color)] bg-[var(--bg-panel)] p-4">
              {sideMessages.length === 0 ? (
                <div className="text-xs text-[var(--text-muted)]">
                  Ask a quick side question without interrupting or cluttering your main coding conversation.
                </div>
              ) : (
                <div className="flex flex-col gap-3">
                  {sideMessages.map((m, idx) => (
                    <div
                      key={idx}
                      className={`rounded-lg px-3 py-2 text-xs leading-relaxed ${
                        m.role === 'user'
                          ? 'ml-auto max-w-[85%] bg-[var(--bg-hover)] text-[var(--text-primary)]'
                          : 'bg-[var(--bg-body)] text-[var(--text-secondary)] whitespace-pre-wrap'
                      }`}
                    >
                      {m.text || '...'}
                    </div>
                  ))}
                </div>
              )}
            </div>
            {sideError && (
              <div
                role="alert"
                className="flex items-center justify-between gap-2 rounded-xl border border-red-500/30 bg-red-500/10 px-3 py-2 text-xs text-red-200"
              >
                <span>{sideError}</span>
                <button
                  type="button"
                  onClick={() => setSideError(null)}
                  className="shrink-0 rounded-md px-1.5 py-0.5 text-[11px] text-red-300 hover:bg-red-500/20 cursor-pointer"
                >
                  Dismiss
                </button>
              </div>
            )}
            <form onSubmit={sendSideQuestion} className="flex items-center gap-2">
              <input
                type="text"
                value={sidePrompt}
                onChange={(e) => setSidePrompt(e.target.value)}
                placeholder="Ask a quick side question..."
                className="h-9 flex-1 rounded-xl border border-[var(--border-color)] bg-[var(--bg-panel)] px-3 text-xs text-[var(--text-primary)] outline-none focus:border-[var(--border-focus)]"
              />
              <button
                type="submit"
                disabled={sideLoading || !sidePrompt.trim()}
                className="inline-flex h-9 items-center gap-1 rounded-xl border border-[var(--border-color)] bg-[var(--bg-panel)] px-3 text-xs font-medium text-[var(--text-primary)] hover:bg-[var(--bg-hover)] disabled:opacity-50 cursor-pointer"
              >
                <ArrowUpIcon size={13} />
                <span>Ask</span>
              </button>
            </form>
          </div>
        )}
      </div>
    </div>
  );
}

export function FloatingMiniChat({
  title,
  messages,
  isExecuting,
  draft,
  onDraftChange,
  onSend,
  onExpandToFull,
}: {
  title: string;
  messages: MiniChatMessage[];
  isExecuting: boolean;
  draft: string;
  onDraftChange: (v: string) => void;
  onSend: () => void;
  onExpandToFull: () => void;
}) {
  const [minimized, setMinimized] = useState(false);
  const [pos, setPos] = useState<{ right: number; bottom: number }>({ right: 20, bottom: 20 });
  const dragRef = useRef<{ startX: number; startY: number; startRight: number; startBottom: number } | null>(null);

  useEffect(() => {
    const onMove = (e: PointerEvent) => {
      if (!dragRef.current) return;
      const dx = e.clientX - dragRef.current.startX;
      const dy = e.clientY - dragRef.current.startY;
      setPos({
        right: Math.max(12, Math.min(window.innerWidth - 220, dragRef.current.startRight - dx)),
        bottom: Math.max(12, Math.min(window.innerHeight - 100, dragRef.current.startBottom - dy)),
      });
    };
    const onUp = () => {
      dragRef.current = null;
    };
    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', onUp);
    return () => {
      window.removeEventListener('pointermove', onMove);
      window.removeEventListener('pointerup', onUp);
    };
  }, []);

  return (
    <div
      style={{ position: 'fixed', right: pos.right, bottom: pos.bottom, zIndex: 9990 }}
      className={`flex flex-col overflow-hidden rounded-2xl border border-[var(--border-color)] bg-[var(--bg-panel)] shadow-2xl ${
        minimized ? 'w-[240px]' : 'w-[340px] max-w-[calc(100vw-24px)]'
      }`}
    >
      <div
        onPointerDown={(e) => {
          dragRef.current = {
            startX: e.clientX,
            startY: e.clientY,
            startRight: pos.right,
            startBottom: pos.bottom,
          };
        }}
        className="flex cursor-grab items-center justify-between gap-2 border-b border-[var(--border-color)] bg-[var(--bg-body)] px-3 py-2 active:cursor-grabbing select-none"
      >
        <div className="flex min-w-0 items-center gap-1.5">
          <GripHorizontalIcon size={13} className="shrink-0 text-[var(--text-muted)]" />
          <span className="truncate text-xs font-medium text-[var(--text-primary)]">{title}</span>
        </div>
        <div className="flex shrink-0 items-center gap-1">
          <button
            type="button"
            title={minimized ? 'Open mini chat' : 'Minimize'}
            onClick={() => setMinimized((v) => !v)}
            className="inline-flex h-5 w-5 items-center justify-center rounded text-[var(--text-muted)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
          >
            <Minimize2Icon size={11} />
          </button>
          <button
            type="button"
            title="Expand chat to center"
            onClick={onExpandToFull}
            className="inline-flex h-5 w-5 items-center justify-center rounded text-[var(--text-muted)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
          >
            <Maximize2Icon size={11} />
          </button>
        </div>
      </div>

      {!minimized && (
        <>
          <div className="flex max-h-[220px] min-h-[120px] flex-col gap-2 overflow-y-auto p-3">
            {messages.slice(-6).map((m) => (
              <div
                key={m.id}
                className={`rounded-xl px-2.5 py-1.5 text-xs leading-snug ${
                  m.role === 'user'
                    ? 'ml-auto max-w-[85%] bg-[var(--bg-hover)] text-[var(--text-primary)]'
                    : 'bg-[var(--bg-body)] text-[var(--text-secondary)] line-clamp-6 whitespace-pre-wrap'
                }`}
              >
                {m.content || (isExecuting ? 'Thinking…' : '')}
              </div>
            ))}
          </div>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              onSend();
            }}
            className="flex items-center gap-1.5 border-t border-[var(--border-color)] p-2"
          >
            <input
              type="text"
              value={draft}
              onChange={(e) => onDraftChange(e.target.value)}
              placeholder="Ask while viewing..."
              className="h-8 min-w-0 flex-1 rounded-lg border border-[var(--border-color)] bg-[var(--bg-body)] px-2.5 text-xs text-[var(--text-primary)] outline-none focus:border-[var(--border-focus)]"
            />
            <button
              type="submit"
              disabled={isExecuting || !draft.trim()}
              className="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-[var(--border-color)] bg-[var(--bg-body)] text-[var(--text-primary)] hover:bg-[var(--bg-hover)] disabled:opacity-40 cursor-pointer"
            >
              <ArrowUpIcon size={13} />
            </button>
          </form>
        </>
      )}
    </div>
  );
}

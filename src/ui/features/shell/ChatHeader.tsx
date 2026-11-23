import {
  FolderIcon,
} from 'lucide-react';
import React from 'react';
import {
  type ChatEntry,
  type DeviceItem,
  type ManagedProcessItem,
} from '../../lib/types';
import {
  type DockSurface,
} from '../workspace/WorkspaceDock';



export interface ChatHeaderProps {
  sidebarOpen: boolean;
  setSidebarOpen: React.Dispatch<React.SetStateAction<boolean>>;
  appMode: "chat" | "code";
  isPlanMode: boolean;
  activeMessages: ChatEntry[];
  activeProjectId: string;
  activeProjectLabel: string;
  devices: DeviceItem[];
  activeDeviceId: string;
  setMissionControlOpen: React.Dispatch<React.SetStateAction<boolean>>;
  setActiveDockSurface: React.Dispatch<React.SetStateAction<DockSurface | null>>;
  managedProcesses: ManagedProcessItem[];
  setProcessesOpen: React.Dispatch<React.SetStateAction<boolean>>;
  loadManagedProcesses: () => Promise<void>;
  processesOpen: boolean;
  handleViewProcessLogs: (processId: string) => Promise<void>;
  handleStopProcess: (processId: string) => Promise<void>;
  selectedProcessLogs: { id: string; logs: string; } | null;
}

export function ChatHeader({ sidebarOpen, setSidebarOpen, appMode, isPlanMode, activeMessages, activeProjectId, activeProjectLabel, devices, activeDeviceId, setMissionControlOpen, setActiveDockSurface, managedProcesses, setProcessesOpen, loadManagedProcesses, processesOpen, handleViewProcessLogs, handleStopProcess, selectedProcessLogs }: ChatHeaderProps) {
  return (
    <header className="chat-header" style={{ position: 'relative' }}>
        <div className="chat-header-left">
          {!sidebarOpen && (
            <button
              className="btn-pane-toggle"
              type="button"
              title="Show chats"
              onClick={() => setSidebarOpen(true)}
            >
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><rect x="3" y="3" width="18" height="18" rx="2" /><path d="M9 3v18" /></svg>
            </button>
          )}
        </div>
        {appMode === 'code' && (isPlanMode || (activeMessages.length > 0 && activeProjectId !== 'local')) && (
          <div
            className="absolute left-1/2 -translate-x-1/2 flex items-center gap-2 text-xs font-medium text-[var(--text-secondary)]"
            title={activeProjectLabel}
          >
            <FolderIcon size={13} />
            <span className="max-w-[200px] truncate">{activeProjectLabel}</span>
            {(() => {
              const activeDev = devices.find((d) => d.deviceId === activeDeviceId);
              const envLabel =
                activeDev?.kind === 'cloud'
                  ? 'Cloud'
                  : activeDev?.kind === 'network'
                    ? 'Network'
                    : 'Local';
              return (
                <button
                  type="button"
                  onClick={() => setMissionControlOpen(true)}
                  title="Open Mission Control"
                  className="rounded-md border border-[var(--border-color)] bg-[var(--bg-panel)] px-1.5 py-0.5 text-[10.5px] font-medium text-[var(--text-muted)] transition-colors hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
                >
                  {envLabel}
                </button>
              );
            })()}
            {isPlanMode && (
              <button
                type="button"
                onClick={() => setActiveDockSurface(null)}
                title="Exit Plan mode (read-only)"
                className="rounded-md border border-sky-500/30 bg-sky-500/10 px-1.5 py-0.5 text-[10.5px] font-medium text-sky-400 transition-colors hover:bg-sky-500/20 cursor-pointer"
              >
                Plan
              </button>
            )}
          </div>
        )}
        {appMode === 'code' && managedProcesses.length > 0 && (
          <div className="relative flex items-center gap-2">
            <button
              type="button"
              className="btn-pane-toggle flex items-center gap-1.5 px-2.5 text-xs"
              onClick={() => {
                setProcessesOpen((v) => !v);
                void loadManagedProcesses();
              }}
              title="Managed background processes"
            >
              <span
                className={`inline-block h-2 w-2 rounded-full ${
                  managedProcesses.some((p) => p.status === 'running')
                    ? 'bg-emerald-400'
                    : 'bg-[var(--text-muted)]'
                }`}
              />
              <span>
                Processes ({managedProcesses.filter((p) => p.status === 'running').length}/
                {managedProcesses.length})
              </span>
            </button>
            {processesOpen && (
              <div className="absolute right-0 top-full z-50 mt-2 w-[380px] rounded-xl border border-[var(--border)] bg-[var(--bg-elevated)] p-3 shadow-xl">
                <div className="mb-2 flex items-center justify-between text-xs font-medium text-[var(--text-secondary)]">
                  <span>Managed Processes</span>
                  <button
                    type="button"
                    className="text-[11px] hover:text-[var(--text-primary)]"
                    onClick={() => setProcessesOpen(false)}
                  >
                    Close
                  </button>
                </div>
                <div className="flex max-h-56 flex-col gap-2 overflow-y-auto">
                  {managedProcesses.map((proc) => (
                    <div
                      key={proc.id}
                      className="rounded-lg border border-[var(--border)] bg-[var(--bg-surface)] p-2 text-xs"
                    >
                      <div className="flex items-center justify-between gap-2">
                        <span className="truncate font-medium text-[var(--text-primary)]">
                          {proc.label}
                        </span>
                        <span
                          className={`shrink-0 rounded px-1.5 py-0.5 text-[10px] font-medium ${
                            proc.status === 'running'
                              ? 'bg-emerald-500/15 text-emerald-400'
                              : 'bg-[var(--bg-hover)] text-[var(--text-muted)]'
                          }`}
                        >
                          {proc.status}
                        </span>
                      </div>
                      <div className="mt-1 truncate font-mono text-[11px] text-[var(--text-muted)]">
                        {proc.command} (pid {proc.pid})
                      </div>
                      <div className="mt-2 flex items-center gap-2">
                        <button
                          type="button"
                          className="rounded border border-[var(--border)] px-2 py-0.5 text-[11px] text-[var(--text-secondary)] hover:text-[var(--text-primary)]"
                          onClick={() => void handleViewProcessLogs(proc.id)}
                        >
                          Logs
                        </button>
                        {proc.status === 'running' && (
                          <button
                            type="button"
                            className="rounded border border-red-500/30 bg-red-500/10 px-2 py-0.5 text-[11px] text-red-400 hover:bg-red-500/20"
                            onClick={() => void handleStopProcess(proc.id)}
                          >
                            Stop
                          </button>
                        )}
                      </div>
                    </div>
                  ))}
                </div>
                {selectedProcessLogs && (
                  <pre className="mt-2 max-h-40 overflow-auto rounded-lg border border-[var(--border)] bg-black/40 p-2 font-mono text-[11px] text-[var(--text-secondary)] whitespace-pre-wrap">
                    {selectedProcessLogs.logs}
                  </pre>
                )}
              </div>
            )}
          </div>
        )}
      </header>
  );
}

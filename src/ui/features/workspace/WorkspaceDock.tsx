import React, { useEffect, useRef, useState } from 'react';
import {
  CheckIcon,
  ClipboardListIcon,
  FolderTreeIcon,
  GitCompareIcon,
  GlobeIcon,
  LayersIcon,
  MessageSquareDashedIcon,
  MonitorIcon,
  TerminalIcon,
} from 'lucide-react';
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '../../components/ui/tooltip';

export type DockSurface = 'plan' | 'agents' | 'files' | 'preview' | 'diff' | 'terminal' | 'side-question';

export interface DockDeviceItem {
  deviceId: string;
  deviceName: string;
  kind?: string;
}

interface WorkspaceDockProps {
  activeSurface: DockSurface | null;
  onToggleSurface: (surface: DockSurface) => void;
  hasPlan?: boolean;
  attentionCount?: number;
  missionControlOpen?: boolean;
  processCount?: number;
  devices?: DockDeviceItem[];
  activeDeviceId?: string;
  onSelectDevice?: (deviceId: string) => void;
}

const DOCK_ITEMS: Array<{
  id: DockSurface;
  label: string;
  icon: React.ComponentType<{ size?: number; strokeWidth?: number }>;
}> = [
  { id: 'files', label: 'Files', icon: FolderTreeIcon },
  { id: 'plan', label: 'Plan', icon: ClipboardListIcon },
  { id: 'agents', label: 'Mission Control', icon: LayersIcon },
  { id: 'preview', label: 'Preview', icon: GlobeIcon },
  { id: 'diff', label: 'Diff', icon: GitCompareIcon },
  { id: 'terminal', label: 'Terminal', icon: TerminalIcon },
  { id: 'side-question', label: 'Side Question', icon: MessageSquareDashedIcon },
];

function formatDeviceLabel(d: DockDeviceItem): string {
  const prefix =
    d.kind === 'cloud'
      ? 'Cloud'
      : d.kind === 'network'
        ? 'Network'
        : 'Local';
  return d.deviceName.toLowerCase().startsWith(prefix.toLowerCase())
    ? d.deviceName
    : `${prefix} · ${d.deviceName}`;
}

export function WorkspaceDock({
  activeSurface,
  onToggleSurface,
  hasPlan = false,
  attentionCount = 0,
  missionControlOpen = false,
  processCount = 0,
  devices = [],
  activeDeviceId,
  onSelectDevice,
}: WorkspaceDockProps) {
  const [envOpen, setEnvOpen] = useState(false);
  const envRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!envOpen) return;
    const onDown = (e: MouseEvent) => {
      if (envRef.current && !envRef.current.contains(e.target as Node)) {
        setEnvOpen(false);
      }
    };
    document.addEventListener('mousedown', onDown);
    return () => document.removeEventListener('mousedown', onDown);
  }, [envOpen]);

  return (
    <TooltipProvider delayDuration={150}>
      <aside
        aria-label="Workspace dock"
        className="flex w-12 shrink-0 flex-col items-center justify-center gap-2 bg-transparent py-3"
      >
        <div ref={envRef} className="relative">
          <Tooltip>
            <TooltipTrigger asChild>
              <button
                type="button"
                aria-label="Environment"
                aria-expanded={envOpen}
                onClick={() => setEnvOpen((v) => !v)}
                className={`relative flex h-9 w-9 items-center justify-center rounded-xl transition-colors cursor-pointer ${
                  envOpen
                    ? 'bg-white/10 text-[var(--text-primary)]'
                    : 'text-[var(--text-muted)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)]'
                }`}
              >
                <MonitorIcon size={17} strokeWidth={1.6} />
              </button>
            </TooltipTrigger>
            <TooltipContent side="right" sideOffset={10}>
              Environment
            </TooltipContent>
          </Tooltip>

          {envOpen && (
            <div
              role="dialog"
              aria-label="Execution environment"
              className="absolute left-[calc(100%+10px)] top-1/2 z-50 w-60 -translate-y-1/2 rounded-xl border border-[var(--border-color)] bg-[var(--bg-panel)] p-1.5 shadow-2xl"
            >
              <div className="px-2.5 py-1.5 text-[11px] font-medium text-[var(--text-muted)]">
                Execution environment
              </div>
              <div className="flex flex-col gap-0.5">
                {devices.length === 0 ? (
                  <div className="px-2.5 py-2 text-xs text-[var(--text-muted)]">
                    Local workspace
                  </div>
                ) : (
                  devices.map((d) => {
                    const selected = d.deviceId === activeDeviceId;
                    return (
                      <button
                        key={d.deviceId}
                        type="button"
                        onClick={() => {
                          onSelectDevice?.(d.deviceId);
                          setEnvOpen(false);
                        }}
                        className={`flex w-full items-center justify-between gap-2 rounded-lg px-2.5 py-1.5 text-left text-xs transition-colors cursor-pointer ${
                          selected
                            ? 'bg-[var(--bg-hover)] font-medium text-[var(--text-primary)]'
                            : 'text-[var(--text-secondary)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)]'
                        }`}
                      >
                        <span className="truncate">{formatDeviceLabel(d)}</span>
                        {selected && <CheckIcon size={12} className="shrink-0 text-[var(--text-primary)]" />}
                      </button>
                    );
                  })
                )}
              </div>
            </div>
          )}
        </div>

        {DOCK_ITEMS.map((item) => {
          const Icon = item.icon;
          const isEnabled = item.id === 'plan' || item.id === 'agents';
          const active =
            (item.id === 'plan' && activeSurface === 'plan') ||
            (item.id === 'agents' && missionControlOpen);
          const badge =
            item.id === 'plan' && hasPlan
              ? '•'
              : item.id === 'agents' && attentionCount > 0
                ? String(attentionCount)
                : null;
          return (
            <Tooltip key={item.id}>
              <TooltipTrigger asChild>
                <button
                  type="button"
                  disabled={!isEnabled}
                  aria-label={item.label}
                  onClick={() => {
                    if (isEnabled) onToggleSurface(item.id);
                  }}
                  className={`relative flex h-9 w-9 items-center justify-center rounded-xl transition-colors ${
                    !isEnabled
                      ? 'text-[var(--text-muted)] opacity-35 cursor-not-allowed'
                      : active
                        ? 'bg-white/10 text-[var(--text-primary)] cursor-pointer'
                        : 'text-[var(--text-muted)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer'
                  }`}
                >
                  <Icon size={17} strokeWidth={1.6} />
                  {badge && (
                    <span className="absolute right-1 top-0.5 text-[9px] font-semibold text-[var(--accent-text)]">
                      {badge}
                    </span>
                  )}
                </button>
              </TooltipTrigger>
              <TooltipContent side="right" sideOffset={10}>
                {isEnabled ? item.label : `${item.label} (Disabled)`}
              </TooltipContent>
            </Tooltip>
          );
        })}
      </aside>
    </TooltipProvider>
  );
}

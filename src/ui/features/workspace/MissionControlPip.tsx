import React, { useMemo, useState } from 'react';
import {
  ArrowUpRightIcon,
  CheckIcon,
  ChevronLeftIcon,
  ChevronRightIcon,
  Maximize2Icon,
  SquareIcon,
  XIcon,
} from 'lucide-react';
import type { IslandActivity } from '../../components/ui/dynamic-island';
import { formatEnvironmentLabel } from './MissionControlDrawer';

export interface MissionControlPipProps {
  activities: IslandActivity[];
  onApproveActivity: (activity: IslandActivity, approved: boolean) => void;
  onStopActivity: (runId: string) => void;
  onSelectActivity?: (activity: IslandActivity) => void;
  onExpandToMain: () => void;
  onClose: () => void;
  embedded?: boolean;
}

function getStatusDotClass(stage: IslandActivity['stage'], needsApproval: boolean): string {
  if (needsApproval) return 'bg-amber-400';
  if (stage === 'executing') return 'bg-[var(--accent)] animate-pulse';
  if (stage === 'completed') return 'bg-emerald-400';
  if (stage === 'failed') return 'bg-red-400';
  return 'bg-[var(--text-muted)]';
}

export function MissionControlPip({
  activities,
  onApproveActivity,
  onStopActivity,
  onSelectActivity,
  onExpandToMain,
  onClose,
  embedded = false,
}: MissionControlPipProps) {
  const ordered = useMemo(() => {
    const score = (a: IslandActivity) => {
      if (a.pendingApproval || a.stage === 'paused') return 0;
      if (a.stage === 'executing') return 1;
      if (a.reviewLayerId || (a.stagedFileCount ?? 0) > 0) return 2;
      return 3;
    };
    return [...activities].sort((a, b) => score(a) - score(b));
  }, [activities]);

  const [cursor, setCursor] = useState(0);
  const safeIndex = ordered.length > 0 ? Math.min(cursor, ordered.length - 1) : 0;
  const active = ordered[safeIndex] ?? null;
  const activeCount = ordered.filter(
    (a) => a.stage === 'executing' || a.stage === 'paused' || Boolean(a.pendingApproval)
  ).length;

  const needsApproval = Boolean(active?.pendingApproval) || active?.stage === 'paused';
  const runningWorkers = active?.workers?.filter((w) => w.status === 'running') ?? [];
  const primaryWorker = runningWorkers[0] ?? active?.workers?.[0];
  const subtitle = active
    ? [
        active.projectLabel || 'Local',
        formatEnvironmentLabel(active),
        active.workers && active.workers.length > 0
          ? `${runningWorkers.length || active.workers.length}/${active.workers.length} workers`
          : null,
      ]
        .filter(Boolean)
        .join(' · ')
    : 'No active agents';

  return (
    <div
      className={`flex flex-col justify-between bg-[var(--bg-stage,#0f1012)] text-[var(--text-primary)] select-none ${
        embedded
          ? 'w-[340px] rounded-xl border border-[var(--border-color)] shadow-2xl'
          : 'h-screen w-screen'
      }`}
    >
      {/* Draggable Spotify-style miniplayer top bar */}
      <div
        data-tauri-drag-region
        className="flex h-8 shrink-0 items-center justify-between border-b border-[var(--border-color)] bg-[var(--bg-panel)] px-2.5"
      >
        <div data-tauri-drag-region className="flex items-center gap-2 min-w-0">
          <span
            className={`h-1.5 w-1.5 shrink-0 rounded-full ${
              active ? getStatusDotClass(active.stage, needsApproval) : 'bg-[var(--text-muted)]'
            }`}
          />
          <span
            data-tauri-drag-region
            className="text-[10px] font-medium uppercase tracking-[0.06em] text-[var(--text-secondary)] tabular-nums truncate"
          >
            {activeCount > 0 ? `${activeCount} active` : 'Gram · Idle'}
          </span>
          {ordered.length > 1 && (
            <div className="flex items-center gap-0.5 text-[10px] text-[var(--text-muted)] tabular-nums">
              <button
                type="button"
                aria-label="Previous agent"
                onClick={() => setCursor((c) => (c - 1 + ordered.length) % ordered.length)}
                className="inline-flex h-5 w-5 items-center justify-center rounded hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
              >
                <ChevronLeftIcon size={12} />
              </button>
              <span>
                {safeIndex + 1}/{ordered.length}
              </span>
              <button
                type="button"
                aria-label="Next agent"
                onClick={() => setCursor((c) => (c + 1) % ordered.length)}
                className="inline-flex h-5 w-5 items-center justify-center rounded hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
              >
                <ChevronRightIcon size={12} />
              </button>
            </div>
          )}
        </div>

        <div className="flex items-center gap-0.5">
          <button
            type="button"
            title="Expand in Gram"
            aria-label="Expand in Gram"
            onClick={onExpandToMain}
            className="inline-flex h-6 w-6 items-center justify-center rounded text-[var(--text-muted)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
          >
            <Maximize2Icon size={11} />
          </button>
          <button
            type="button"
            title="Close miniplayer"
            aria-label="Close miniplayer"
            onClick={onClose}
            className="inline-flex h-6 w-6 items-center justify-center rounded text-[var(--text-muted)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
          >
            <XIcon size={12} />
          </button>
        </div>
      </div>

      {/* Main track / agent status */}
      <div className="flex flex-1 flex-col justify-center gap-1 px-3 py-2 min-w-0">
        {active ? (
          <>
            <div className="truncate text-[13px] font-medium leading-tight text-[var(--text-primary)]">
              {active.title}
            </div>
            <div className="truncate text-[11px] text-[var(--text-muted)] tabular-nums">
              {subtitle}
            </div>
            {(active.pendingApproval?.command || primaryWorker?.currentStep) && (
              <div className="truncate font-mono text-[10.5px] text-[var(--text-secondary)]">
                {active.pendingApproval?.command
                  ? `$ ${active.pendingApproval.command}`
                  : primaryWorker?.currentStep}
              </div>
            )}
          </>
        ) : (
          <div className="text-xs text-[var(--text-muted)]">
            No active or recent agent runs.
          </div>
        )}
      </div>

      {/* Transport bar */}
      {active && (
        <div className="flex h-9 shrink-0 items-center justify-between border-t border-[var(--border-color)] bg-[var(--bg-panel)] px-2.5">
          <span className="text-[10.5px] text-[var(--text-muted)] tabular-nums">
            {needsApproval
              ? 'Needs approval'
              : active.stage === 'executing'
              ? 'Running'
              : active.stage === 'completed'
              ? 'Completed'
              : active.stage === 'failed'
              ? 'Failed'
              : 'Idle'}
          </span>
          <div className="flex items-center gap-1.5">
            {needsApproval && (
              <>
                <button
                  type="button"
                  onClick={() => onApproveActivity(active, true)}
                  className="inline-flex items-center gap-1 rounded border border-[var(--border-color)] bg-[var(--bg-active)] px-2 py-0.5 text-[11px] font-medium text-[var(--text-primary)] hover:opacity-90 cursor-pointer"
                >
                  <CheckIcon size={11} />
                  <span>Approve</span>
                </button>
                <button
                  type="button"
                  onClick={() => onApproveActivity(active, false)}
                  className="inline-flex items-center rounded px-2 py-0.5 text-[11px] text-[var(--text-secondary)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
                >
                  Deny
                </button>
              </>
            )}
            {active.stage === 'executing' && (
              <button
                type="button"
                onClick={() => onStopActivity(active.runId)}
                className="inline-flex items-center gap-1 rounded px-2 py-0.5 text-[11px] text-red-400 hover:bg-[var(--bg-hover)] cursor-pointer"
              >
                <SquareIcon size={10} />
                <span>Stop</span>
              </button>
            )}
            <button
              type="button"
              onClick={() => {
                onSelectActivity?.(active);
                onExpandToMain();
              }}
              className="inline-flex items-center gap-0.5 rounded px-1.5 py-0.5 text-[11px] text-[var(--text-secondary)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
            >
              <span>Open</span>
              <ArrowUpRightIcon size={11} />
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

import React, { useMemo, useState } from 'react';
import {
  CheckIcon,
  ChevronDownIcon,
  ChevronRightIcon,
  PictureInPicture2Icon,
  SquareIcon,
  XIcon,
} from 'lucide-react';
import type { IslandActivity } from '../../components/ui/dynamic-island';
import type { ExecutionEnvironment } from '../../lib/types';

export interface MissionControlProjectGroup {
  projectKey: string;
  projectLabel: string;
  items: IslandActivity[];
}

export function resolveActivityEnvironment(item: IslandActivity): ExecutionEnvironment {
  if (item.environment) return item.environment;
  if (item.worktreePath) return 'worktree';
  if (item.kind === 'swarm' || (item.workers?.length ?? 0) > 0) return 'cloud-lyra';
  return 'local';
}

export function formatEnvironmentLabel(item: IslandActivity): string {
  const env = resolveActivityEnvironment(item);
  if (env === 'cloud-lyra') return 'Cloud · Lyra';
  if (env === 'worktree') return 'Worktree';
  if (env === 'network') return 'Network';
  return 'Local';
}

export function splitAttentionActivities(activities: IslandActivity[]): {
  attention: IslandActivity[];
  grouped: MissionControlProjectGroup[];
} {
  const attention: IslandActivity[] = [];
  const byProject = new Map<string, MissionControlProjectGroup>();

  for (const item of activities) {
    const needsApproval = Boolean(item.pendingApproval) || item.stage === 'paused';
    const hasUnreviewedOverlay = Boolean(item.reviewLayerId) || (item.stagedFileCount ?? 0) > 0;
    if (needsApproval || (hasUnreviewedOverlay && item.stage !== 'executing')) {
      attention.push(item);
    }

    const projectKey = item.projectId || 'local';
    const projectLabel = item.projectLabel || 'Local workspace';
    let group = byProject.get(projectKey);
    if (!group) {
      group = { projectKey, projectLabel, items: [] };
      byProject.set(projectKey, group);
    }
    group.items.push(item);
  }

  return {
    attention,
    grouped: Array.from(byProject.values()),
  };
}

interface MissionControlDrawerProps {
  open: boolean;
  onClose: () => void;
  activities: IslandActivity[];
  onSelectActivity: (activity: IslandActivity) => void;
  onApproveActivity: (activity: IslandActivity, approved: boolean) => void;
  onStopActivity: (runId: string) => void;
  onOpenDiff?: (activity: IslandActivity) => void;
  onPopOutPip?: () => void;
}

function statusDotClass(stage: IslandActivity['stage'], needsApproval = false): string {
  if (needsApproval) return 'bg-amber-400';
  if (stage === 'executing') return 'bg-[var(--accent)] animate-pulse';
  if (stage === 'completed') return 'bg-emerald-400';
  if (stage === 'failed') return 'bg-red-400';
  return 'bg-[var(--text-muted)]';
}

export function MissionControlDrawer({
  open,
  onClose,
  activities,
  onSelectActivity,
  onApproveActivity,
  onStopActivity,
  onOpenDiff,
  onPopOutPip,
}: MissionControlDrawerProps) {
  const [expandedWorkers, setExpandedWorkers] = useState<Record<string, boolean>>({});
  const { attention, grouped } = useMemo(
    () => splitAttentionActivities(activities),
    [activities]
  );

  if (!open) return null;

  const activeCount = activities.filter(
    (a) => a.stage === 'executing' || a.stage === 'paused' || Boolean(a.pendingApproval)
  ).length;

  return (
    <div
      className="fixed inset-0 z-[110] flex justify-end bg-black/40"
      onClick={onClose}
    >
      <section
        role="dialog"
        aria-label="Mission Control"
        onClick={(e) => e.stopPropagation()}
        className="flex h-full w-full max-w-[440px] flex-col border-l border-[var(--border-color)] bg-[var(--bg-body)] text-[var(--text-primary)]"
      >
        {/* Minimal Linear-style header */}
        <header className="flex h-11 shrink-0 items-center justify-between border-b border-[var(--border-color)] bg-[var(--bg-panel)] px-4">
          <div className="flex items-center gap-2">
            <span
              className={`h-1.5 w-1.5 rounded-full ${
                activeCount > 0 ? 'bg-[var(--accent)] animate-pulse' : 'bg-[var(--text-muted)]'
              }`}
            />
            <h2 className="text-[13px] font-medium tracking-tight text-[var(--text-primary)]">
              Mission Control
            </h2>
            <span className="text-[11px] text-[var(--text-muted)] tabular-nums">
              {activeCount > 0 ? `${activeCount} active` : `${activities.length} total`}
            </span>
          </div>

          <div className="flex items-center gap-1">
            {onPopOutPip && (
              <button
                type="button"
                onClick={onPopOutPip}
                title="Pop out miniplayer (PiP)"
                aria-label="Pop out miniplayer"
                className="inline-flex h-7 items-center gap-1.5 rounded-md px-2 text-[11px] text-[var(--text-secondary)] transition-colors hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
              >
                <PictureInPicture2Icon size={13} />
                <span>Miniplayer</span>
              </button>
            )}
            <button
              type="button"
              onClick={onClose}
              aria-label="Close Mission Control"
              className="inline-flex h-7 w-7 items-center justify-center rounded-md text-[var(--text-muted)] transition-colors hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
            >
              <XIcon size={14} />
            </button>
          </div>
        </header>

        <div className="flex-1 overflow-y-auto">
          {/* Needs Attention: flat hairline rows with a 2px amber left accent */}
          {attention.length > 0 && (
            <section className="border-b border-[var(--border-color)]">
              <div className="flex items-center justify-between px-4 py-2 text-[10px] font-medium uppercase tracking-[0.06em] text-amber-400">
                <span>Needs Attention ({attention.length})</span>
              </div>
              <div className="divide-y divide-[var(--border-color)] border-t border-[var(--border-color)]">
                {attention.map((item) => {
                  const needsApproval = Boolean(item.pendingApproval) || item.stage === 'paused';
                  return (
                    <div
                      key={`attn-${item.runId}`}
                      className="flex items-start justify-between gap-3 border-l-2 border-l-amber-400 px-4 py-2.5 hover:bg-[var(--bg-hover)]"
                    >
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center gap-2">
                          <span className="truncate text-xs font-medium text-[var(--text-primary)]">
                            {item.title}
                          </span>
                          <span className="shrink-0 text-[10.5px] text-[var(--text-muted)]">
                            {item.projectLabel || 'Local'} · {formatEnvironmentLabel(item)}
                          </span>
                        </div>
                        {needsApproval && item.pendingApproval?.command && (
                          <div className="mt-1 truncate font-mono text-[11px] text-[var(--text-secondary)]">
                            $ {item.pendingApproval.command}
                          </div>
                        )}
                        {!needsApproval && (item.stagedFileCount ?? 0) > 0 && (
                          <div className="mt-0.5 text-[11px] text-[var(--text-muted)] tabular-nums">
                            {item.stagedFileCount} staged file{item.stagedFileCount === 1 ? '' : 's'}
                          </div>
                        )}
                      </div>

                      <div className="flex shrink-0 items-center gap-1">
                        {needsApproval ? (
                          <>
                            <button
                              type="button"
                              onClick={() => onApproveActivity(item, true)}
                              className="inline-flex items-center gap-1 rounded border border-[var(--border-color)] bg-[var(--bg-active)] px-2 py-1 text-[11px] font-medium text-[var(--text-primary)] hover:opacity-90 cursor-pointer"
                            >
                              <CheckIcon size={11} />
                              <span>Approve</span>
                            </button>
                            <button
                              type="button"
                              onClick={() => onApproveActivity(item, false)}
                              className="inline-flex items-center rounded px-2 py-1 text-[11px] text-[var(--text-secondary)] hover:bg-[var(--bg-panel)] hover:text-[var(--text-primary)] cursor-pointer"
                            >
                              Deny
                            </button>
                          </>
                        ) : (
                          onOpenDiff && (
                            <button
                              type="button"
                              onClick={() => onOpenDiff(item)}
                              className="inline-flex items-center rounded border border-[var(--border-color)] px-2 py-1 text-[11px] text-[var(--text-primary)] hover:bg-[var(--bg-panel)] cursor-pointer"
                            >
                              Review Diff
                            </button>
                          )
                        )}
                      </div>
                    </div>
                  );
                })}
              </div>
            </section>
          )}

          {/* Empty state */}
          {grouped.length === 0 ? (
            <div className="px-4 py-12 text-center text-xs text-[var(--text-muted)]">
              No active or recent agent runs.
            </div>
          ) : (
            grouped.map((group) => (
              <section key={group.projectKey} className="border-b border-[var(--border-color)]">
                <div className="flex items-center justify-between bg-[var(--bg-panel)]/50 px-4 py-1.5 text-[10px] font-medium uppercase tracking-[0.06em] text-[var(--text-muted)]">
                  <span className="truncate">{group.projectLabel}</span>
                  <span className="tabular-nums">{group.items.length}</span>
                </div>
                <div className="divide-y divide-[var(--border-color)] border-t border-[var(--border-color)]">
                  {group.items.map((item) => {
                    const workers = item.workers ?? [];
                    const isSwarmExpanded = Boolean(expandedWorkers[item.runId]);
                    const runningCount = workers.filter((w) => w.status === 'running').length;
                    const needsApproval = Boolean(item.pendingApproval) || item.stage === 'paused';

                    return (
                      <div key={item.runId} className="group">
                        <div className="flex min-h-[36px] items-center justify-between gap-2 px-4 py-2 hover:bg-[var(--bg-hover)]">
                          <button
                            type="button"
                            onClick={() => {
                              onSelectActivity(item);
                              onClose();
                            }}
                            className="flex min-w-0 flex-1 items-center gap-2.5 text-left cursor-pointer"
                          >
                            <span
                              className={`h-1.5 w-1.5 shrink-0 rounded-full ${statusDotClass(
                                item.stage,
                                needsApproval
                              )}`}
                            />
                            <span className="truncate text-xs text-[var(--text-primary)]">
                              {item.title}
                            </span>
                            <span className="shrink-0 text-[10.5px] text-[var(--text-muted)] tabular-nums">
                              {formatEnvironmentLabel(item)}
                              {workers.length > 0 ? ` · ${runningCount || workers.length}/${workers.length}` : ''}
                            </span>
                          </button>

                          <div className="flex shrink-0 items-center gap-1">
                            {workers.length > 0 && (
                              <button
                                type="button"
                                onClick={() =>
                                  setExpandedWorkers((prev) => ({
                                    ...prev,
                                    [item.runId]: !prev[item.runId],
                                  }))
                                }
                                aria-label="Toggle workers"
                                className="inline-flex h-6 w-6 items-center justify-center rounded text-[var(--text-muted)] hover:bg-[var(--bg-panel)] hover:text-[var(--text-primary)] cursor-pointer"
                              >
                                {isSwarmExpanded ? (
                                  <ChevronDownIcon size={12} />
                                ) : (
                                  <ChevronRightIcon size={12} />
                                )}
                              </button>
                            )}
                            {item.stage === 'executing' && (
                              <button
                                type="button"
                                onClick={() => onStopActivity(item.runId)}
                                title="Stop run"
                                aria-label="Stop run"
                                className="inline-flex h-6 items-center gap-1 rounded px-1.5 text-[11px] text-red-400 hover:bg-[var(--bg-panel)] cursor-pointer"
                              >
                                <SquareIcon size={9} />
                                <span>Stop</span>
                              </button>
                            )}
                          </div>
                        </div>

                        {workers.length > 0 && isSwarmExpanded && (
                          <div className="ml-6 border-l border-[var(--border-color)] pl-3 pb-2 flex flex-col gap-1">
                            {workers.map((worker) => (
                              <div
                                key={worker.id}
                                className="flex items-center justify-between gap-2 pr-4 py-0.5 text-[11px]"
                              >
                                <div className="flex min-w-0 items-center gap-2">
                                  <span
                                    className={`h-1.5 w-1.5 shrink-0 rounded-full ${
                                      worker.status === 'running'
                                        ? 'bg-[var(--accent)] animate-pulse'
                                        : worker.status === 'completed'
                                        ? 'bg-emerald-400'
                                        : worker.status === 'failed'
                                        ? 'bg-red-400'
                                        : 'bg-[var(--text-muted)]'
                                    }`}
                                  />
                                  <span className="truncate font-medium text-[var(--text-secondary)]">
                                    {worker.name}
                                  </span>
                                  {worker.currentStep && (
                                    <span className="truncate text-[var(--text-muted)]">
                                      {worker.currentStep}
                                    </span>
                                  )}
                                </div>
                                <span className="shrink-0 font-mono text-[10px] text-[var(--text-muted)]">
                                  {worker.model}
                                </span>
                              </div>
                            ))}
                          </div>
                        )}
                      </div>
                    );
                  })}
                </div>
              </section>
            ))
          )}
        </div>
      </section>
    </div>
  );
}

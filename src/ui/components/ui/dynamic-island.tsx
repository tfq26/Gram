import React, { useEffect, useRef, useState } from 'react';
import {
  CheckIcon,
  ChevronDownIcon,
  CircleCheckIcon,
  LayersIcon,
  LoaderCircleIcon,
  SquareIcon,
  XIcon,
} from 'lucide-react';
import { cn } from '@/lib/utils';
import type { ExecutionEnvironment, WorkerTelemetry } from '../../lib/types';

export interface IslandActivity {
  runId: string;
  sessionId: string;
  title: string;
  prompt?: string;
  output?: string;
  stage: 'idle' | 'executing' | 'paused' | 'completed' | 'failed' | string;
  projectId?: string;
  projectLabel?: string;
  kind?: 'agent' | 'swarm';
  environment?: ExecutionEnvironment;
  modelId?: string;
  stagedFileCount?: number;
  reviewLayerId?: string;
  workers?: WorkerTelemetry[];
  pendingApproval?: {
    id: string;
    title: string;
    command?: string;
    description?: string;
  };
  activePlan?: Array<{ id: string; title: string; status: string }>;
  completedTaskIds?: string[];
  modifiedFiles?: string[];
  lastVerification?: { command: string; passed: boolean; summary: string; timestamp: string };
  worktreePath?: string;
  isActive?: boolean;
  updatedAt?: string;
}

function getPriority(item: IslandActivity): number {
  if (item.pendingApproval || item.stage === 'paused') return 4;
  if (item.stage === 'executing' || item.isActive) return 3;
  if (item.kind === 'swarm' || (item.workers?.length ?? 0) > 0) return 2;
  return 1;
}

function getProgress(item: IslandActivity): { completed: number; total: number; percent: number } {
  if (item.workers && item.workers.length > 0) {
    const total = item.workers.length;
    const completed = item.workers.filter((w) => w.status === 'completed' || w.status === 'done').length;
    const percent = item.stage === 'completed' ? 100 : Math.max(15, Math.round((completed / total) * 100));
    return { completed, total, percent };
  }
  const plan = item.activePlan || [];
  if (plan.length > 0) {
    const completed = plan.filter((p) => p.status === 'completed').length;
    const percent =
      item.stage === 'completed'
        ? 100
        : item.stage === 'executing' || item.isActive
          ? Math.max(25, Math.round((completed / plan.length) * 100))
          : Math.round((completed / plan.length) * 100);
    return { completed, total: plan.length, percent };
  }
  return {
    completed: item.stage === 'completed' ? 1 : 0,
    total: 1,
    percent: item.stage === 'completed' ? 100 : item.stage === 'executing' || item.isActive ? 45 : 20,
  };
}

function cleanTaskTitle(raw: string): string {
  return String(raw || '')
    .trim()
    .replace(/^["'“”‘’`]+|["'“”‘’`]+$/g, '')
    .replace(/\s+/g, ' ')
    .trim() || 'Untitled task';
}

function getStageLabel(item: IslandActivity): string {
  if (item.pendingApproval || item.stage === 'paused') return 'Needs approval';
  if (item.stage === 'executing' || item.isActive) return 'In progress';
  if (item.stage === 'failed') return 'Stopped';
  return 'Completed';
}

const ACKNOWLEDGE_TTL_MS = 60_000;
const TASK_STALE_TTL_MS = 60 * 60 * 1000;
const ACKNOWLEDGE_STORAGE_KEY = 'gram.island.acknowledged';

function parseTaskTimestamp(updatedAt?: string): number | null {
  if (!updatedAt) return null;
  const parsed = Date.parse(updatedAt);
  return Number.isFinite(parsed) ? parsed : null;
}

function formatTaskTimestamp(updatedAt: string | undefined, now: number): { relative: string; exact: string } | null {
  const ts = parseTaskTimestamp(updatedAt);
  if (ts === null) return null;
  const diffMs = Math.max(0, now - ts);
  const diffSec = Math.floor(diffMs / 1000);
  let relative: string;
  if (diffSec < 45) {
    relative = 'just now';
  } else if (diffSec < 3600) {
    relative = `${Math.floor(diffSec / 60)}m ago`;
  } else if (diffSec < 86400) {
    relative = `${Math.floor(diffSec / 3600)}h ago`;
  } else {
    relative = `${Math.floor(diffSec / 86400)}d ago`;
  }
  const exact = new Date(ts).toLocaleString([], {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  });
  return { relative, exact };
}

export function DynamicIslandBar({
  activities,
  activeSessionId,
  onSelectActivity,
  onApproveActivity,
  onStopActivity,
  onContinueActivity,
  onOpenMissionControl,
}: {
  activities: IslandActivity[];
  activeSessionId: string;
  onSelectActivity: (activity: IslandActivity) => void;
  onApproveActivity: (activity: IslandActivity, approved: boolean) => void;
  onStopActivity: (runId: string) => void;
  onContinueActivity?: (activity: IslandActivity, feedback: string) => Promise<void> | void;
  onOpenMissionControl?: () => void;
}) {
  const [expandedRunId, setExpandedRunId] = useState<string | null>(null);
  const [overflowOpen, setOverflowOpen] = useState(false);
  const [steerDraft, setSteerDraft] = useState('');
  const [steering, setSteering] = useState(false);
  const [now, setNow] = useState(() => Date.now());
  const [acknowledgedAt, setAcknowledgedAt] = useState<Record<string, number>>(() => {
    try {
      const raw = localStorage.getItem(ACKNOWLEDGE_STORAGE_KEY);
      const parsed = raw ? JSON.parse(raw) : null;
      return parsed && typeof parsed === 'object' ? parsed : {};
    } catch {
      return {};
    }
  });
  const containerRef = useRef<HTMLDivElement>(null);

  function acknowledgeRun(runId: string) {
    if (!runId) return;
    setAcknowledgedAt((prev) => {
      if (prev[runId]) return prev;
      const next = { ...prev, [runId]: Date.now() };
      try {
        localStorage.setItem(ACKNOWLEDGE_STORAGE_KEY, JSON.stringify(next));
      } catch {}
      return next;
    });
  }

  useEffect(() => {
    for (const item of activities) {
      const needsApproval = Boolean(item.pendingApproval || item.stage === 'paused');
      const isRunning = Boolean(item.stage === 'executing' || item.isActive);
      if (!isRunning && !needsApproval && item.sessionId && item.sessionId === activeSessionId) {
        acknowledgeRun(item.runId);
      }
    }
  }, [activities, activeSessionId]);

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 5000);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    function handleOutside(e: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setExpandedRunId(null);
        setOverflowOpen(false);
      }
    }
    document.addEventListener('mousedown', handleOutside);
    return () => document.removeEventListener('mousedown', handleOutside);
  }, []);

  const activeActivities = activities.filter((item) => {
    const needsApproval = Boolean(item.pendingApproval || item.stage === 'paused');
    const isRunning = Boolean(item.stage === 'executing' || item.isActive);
    if (isRunning || needsApproval) return true;
    const updatedTs = parseTaskTimestamp(item.updatedAt);
    if (updatedTs !== null && now - updatedTs > TASK_STALE_TTL_MS) {
      return false;
    }
    const ackTime = acknowledgedAt[item.runId];
    if (!ackTime) return true;
    return now - ackTime < ACKNOWLEDGE_TTL_MS;
  });

  if (activeActivities.length === 0) {
    return null;
  }

  const sorted = [...activeActivities].sort((a, b) => getPriority(b) - getPriority(a));
  const visible = sorted.slice(0, 3);
  const overflow = sorted.slice(3);

  return (
    <div
      ref={containerRef}
      className="relative flex min-w-0 items-center gap-2"
      data-slot="dynamic-island-bar"
    >
      {visible.map((item) => {
        const needsApproval = Boolean(item.pendingApproval || item.stage === 'paused');
        const isRunning = Boolean(item.stage === 'executing' || item.isActive);
        const isSwarm = item.kind === 'swarm' || (item.workers?.length ?? 0) > 0;
        const isCurrentChat = Boolean(item.sessionId && item.sessionId === activeSessionId);
        const isExpanded = expandedRunId === item.runId;
        const progress = getProgress(item);
        const showProject = item.projectLabel && item.projectLabel !== 'Local workspace';
        const timeInfo = formatTaskTimestamp(item.updatedAt, now);
        const displayTitle = cleanTaskTitle(item.title);
        const showStepCount =
          (isSwarm && Boolean(item.workers?.length)) ||
          (isRunning && progress.total > 1);

        return (
          <div key={item.runId} className="relative flex items-center">
            <div
              className={cn(
                'relative flex h-[30px] items-center gap-1.5 overflow-hidden rounded-full border px-3 text-xs transition-all duration-200',
                needsApproval
                  ? 'border-[color-mix(in_srgb,var(--warning)_50%,var(--border-color))] bg-[var(--bg-panel)]'
                  : isRunning
                    ? 'border-[color-mix(in_srgb,var(--accent)_45%,var(--border-color))] bg-[var(--bg-panel)]'
                    : isCurrentChat
                      ? 'border-[var(--border-focus)] bg-[var(--bg-panel-subtle)]'
                      : 'border-[var(--border-color)] bg-[var(--bg-panel)] hover:border-[var(--border-focus)] hover:bg-[var(--bg-hover)]'
              )}
            >
              {isRunning && (
                <span
                  aria-hidden="true"
                  className="pointer-events-none absolute inset-x-0 bottom-0 h-[2px] origin-left transition-transform duration-300 ease-out"
                  style={{
                    backgroundColor: 'var(--accent)',
                    opacity: 0.75,
                    transform: `scaleX(${Math.max(0.08, Math.min(1, progress.percent / 100))}) translateZ(0)`,
                  }}
                />
              )}

              <button
                type="button"
                className="flex min-w-0 items-center gap-2 bg-transparent text-left focus:outline-none"
                onClick={() => {
                  acknowledgeRun(item.runId);
                  onSelectActivity(item);
                }}
                title={`${showProject ? `${item.projectLabel} · ` : ''}${displayTitle}${timeInfo ? ` (${timeInfo.relative})` : ''}`}
              >
                {needsApproval ? (
                  <span
                    className="h-2 w-2 shrink-0 rounded-full animate-pulse"
                    style={{ backgroundColor: 'var(--warning)' }}
                  />
                ) : isRunning ? (
                  <LoaderCircleIcon
                    size={12}
                    className="shrink-0 animate-spin"
                    style={{ color: 'var(--accent-text)' }}
                  />
                ) : isSwarm ? (
                  <LayersIcon size={12} className="shrink-0 text-[var(--text-secondary)]" />
                ) : (
                  <CircleCheckIcon
                    size={12}
                    className="shrink-0"
                    style={{ color: item.stage === 'failed' ? 'var(--danger)' : 'var(--success)' }}
                  />
                )}

                <span className="max-w-[210px] truncate font-medium text-[var(--text-primary)]">
                  {displayTitle}
                </span>

                {showProject && !isCurrentChat && (
                  <span className="hidden max-w-[96px] truncate rounded-full bg-[var(--bg-panel-subtle)] px-1.5 py-0.5 text-[10px] text-[var(--text-muted)] sm:inline-block">
                    {item.projectLabel}
                  </span>
                )}

                {showStepCount && (
                  <span className="shrink-0 rounded-full bg-[var(--bg-panel-subtle)] px-1.5 py-0.5 text-[10px] font-medium tabular-nums text-[var(--text-secondary)]">
                    {isSwarm && item.workers?.length
                      ? `${item.workers.length}w`
                      : `${progress.completed}/${progress.total}`}
                  </span>
                )}
              </button>

              {needsApproval && (
                <div className="ml-0.5 flex items-center gap-1 border-l border-[var(--border-color)] pl-1.5">
                  <button
                    type="button"
                    aria-label="Approve task"
                    title="Approve"
                    className="inline-flex h-5 w-5 items-center justify-center rounded-full transition-colors hover:bg-[var(--bg-hover)]"
                    style={{ color: 'var(--success)' }}
                    onClick={(e) => {
                      e.stopPropagation();
                      acknowledgeRun(item.runId);
                      onApproveActivity(item, true);
                    }}
                  >
                    <CheckIcon size={12} />
                  </button>
                  <button
                    type="button"
                    aria-label="Deny task"
                    title="Deny"
                    className="inline-flex h-5 w-5 items-center justify-center rounded-full transition-colors hover:bg-[var(--bg-hover)]"
                    style={{ color: 'var(--danger)' }}
                    onClick={(e) => {
                      e.stopPropagation();
                      acknowledgeRun(item.runId);
                      onApproveActivity(item, false);
                    }}
                  >
                    <XIcon size={12} />
                  </button>
                </div>
              )}

              <button
                type="button"
                aria-label="Expand activity details"
                className="ml-0.5 inline-flex h-5 w-5 items-center justify-center rounded-full text-[var(--text-muted)] transition-colors hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)]"
                onClick={(e) => {
                  e.stopPropagation();
                  acknowledgeRun(item.runId);
                  setOverflowOpen(false);
                  setExpandedRunId((prev) => (prev === item.runId ? null : item.runId));
                }}
              >
                <ChevronDownIcon
                  size={12}
                  className={cn('transition-transform duration-200', isExpanded && 'rotate-180')}
                />
              </button>
            </div>

            {isExpanded && (
              <div
                className="gram-msg-enter absolute left-0 top-[calc(100%+8px)] z-[200] w-[328px] rounded-2xl border border-[var(--border-focus)] bg-[var(--bg-panel)] p-3.5 shadow-[0_20px_50px_rgba(0,0,0,0.65)]"
                style={{ backgroundColor: 'var(--bg-panel)' }}
                data-slot="dynamic-island-widget"
              >
                <div className="flex items-center justify-between gap-2 text-[11px] text-[var(--text-muted)]">
                  <span className="min-w-0 flex-1 truncate font-medium text-[var(--text-secondary)]">
                    {item.projectLabel || 'Local workspace'}
                    {isSwarm ? ' · Swarm' : ''}
                  </span>
                  <span
                    className="shrink-0 whitespace-nowrap font-medium"
                    title={timeInfo?.exact}
                    style={{
                      color: needsApproval
                        ? 'var(--warning)'
                        : isRunning
                          ? 'var(--accent-text)'
                          : item.stage === 'failed'
                            ? 'var(--danger)'
                            : 'var(--text-secondary)',
                    }}
                  >
                    {getStageLabel(item)}
                    {timeInfo ? ` · ${timeInfo.relative}` : ''}
                  </span>
                </div>

                <div className="mt-1.5 line-clamp-2 text-xs font-medium leading-snug text-[var(--text-primary)]">
                  {displayTitle}
                </div>

                {(isRunning || needsApproval) && (
                  <div className="mt-2.5 h-1 w-full overflow-hidden rounded-full bg-[var(--bg-panel-subtle)]">
                    <div
                      className="h-full rounded-full transition-all duration-300"
                      style={{
                        width: `${progress.percent}%`,
                        backgroundColor: needsApproval ? 'var(--warning)' : 'var(--accent-text)',
                      }}
                    />
                  </div>
                )}

                {item.workers && item.workers.length > 0 ? (
                  <div className="mt-2.5 flex max-h-[140px] flex-col gap-1 overflow-y-auto">
                    {item.workers.map((w, idx) => (
                      <div
                        key={w.id || idx}
                        className="flex items-center justify-between rounded-lg bg-[var(--bg-panel)] px-2.5 py-1 text-[11px]"
                      >
                        <span className="truncate text-[var(--text-primary)]">{w.name || w.id}</span>
                        <span className="text-[var(--text-muted)]">{w.status || 'running'}</span>
                      </div>
                    ))}
                  </div>
                ) : (
                  item.activePlan &&
                  (item.activePlan.length > 1 ||
                    (item.activePlan.length === 1 &&
                      item.activePlan[0]?.title.trim() !== (item.prompt || item.title).trim())) && (
                    <div className="mt-2.5 flex max-h-[140px] flex-col gap-1 overflow-y-auto">
                      {item.activePlan.map((step) => (
                        <div
                          key={step.id}
                          className="flex items-center justify-between gap-2 rounded-lg bg-[var(--bg-panel)] px-2.5 py-1 text-[11px]"
                        >
                          <span className="truncate text-[var(--text-primary)]">{step.title}</span>
                          <span className="shrink-0 text-[var(--text-muted)]">{step.status}</span>
                        </div>
                      ))}
                    </div>
                  )
                )}

                {item.pendingApproval && (
                  <div className="mt-3 rounded-xl border border-[color-mix(in_srgb,var(--warning)_35%,var(--border-color))] bg-[var(--bg-panel)] p-2.5">
                    <div className="text-[11px] font-medium text-[var(--text-primary)]">
                      {item.pendingApproval.title}
                    </div>
                    <div className="mt-1 truncate font-mono text-[11px] text-[var(--text-secondary)]">
                      {item.pendingApproval.command || item.title}
                    </div>
                    <div className="mt-2.5 flex items-center gap-2">
                      <button
                        type="button"
                        className="inline-flex flex-1 items-center justify-center gap-1 rounded-lg px-2.5 py-1.5 text-xs font-medium transition-opacity hover:opacity-90"
                        style={{
                          backgroundColor: 'var(--btn-primary-bg)',
                          color: 'var(--btn-primary-text)',
                        }}
                        onClick={() => {
                          onApproveActivity(item, true);
                          setExpandedRunId(null);
                        }}
                      >
                        <CheckIcon size={12} />
                        <span>Approve</span>
                      </button>
                      <button
                        type="button"
                        className="inline-flex flex-1 items-center justify-center gap-1 rounded-lg border border-[var(--border-color)] bg-transparent px-2.5 py-1.5 text-xs font-medium text-[var(--text-secondary)] transition-colors hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)]"
                        onClick={() => {
                          onApproveActivity(item, false);
                          setExpandedRunId(null);
                        }}
                      >
                        <XIcon size={12} />
                        <span>Deny</span>
                      </button>
                    </div>
                  </div>
                )}

                {(item.lastVerification || (item.modifiedFiles && item.modifiedFiles.length > 0) || item.worktreePath) && (
                  <div className="mt-2.5 flex flex-col gap-1.5 rounded-xl border border-[var(--border-color)] bg-[var(--bg-panel)] p-2 text-[11px]">
                    {item.lastVerification && (
                      <div className="flex items-center justify-between gap-2">
                        <span className="truncate font-mono text-[var(--text-secondary)]" title={item.lastVerification.command}>
                          {item.lastVerification.command}
                        </span>
                        <span
                          className="shrink-0 font-medium"
                          style={{ color: item.lastVerification.passed ? 'var(--success)' : 'var(--danger)' }}
                        >
                          {item.lastVerification.passed ? 'Verified' : 'Failed'}
                        </span>
                      </div>
                    )}
                    {item.modifiedFiles && item.modifiedFiles.length > 0 && (
                      <div className="truncate text-[var(--text-muted)]" title={item.modifiedFiles.join(', ')}>
                        {item.modifiedFiles.length} file{item.modifiedFiles.length === 1 ? '' : 's'}:{' '}
                        <span className="font-mono text-[var(--text-secondary)]">
                          {item.modifiedFiles.slice(0, 3).map((f) => f.split('/').pop()).join(', ')}
                          {item.modifiedFiles.length > 3 ? ` +${item.modifiedFiles.length - 3}` : ''}
                        </span>
                      </div>
                    )}
                    {item.worktreePath && (
                      <div className="truncate text-[10px] text-[var(--text-muted)]">
                        Isolated worktree
                      </div>
                    )}
                  </div>
                )}

                {onContinueActivity && !isRunning && !needsApproval && (
                  <form
                    className="mt-2.5 flex items-center gap-1.5"
                    onSubmit={async (e) => {
                      e.preventDefault();
                      const text = steerDraft.trim();
                      if (!text || steering) return;
                      setSteering(true);
                      try {
                        await onContinueActivity(item, text);
                        setSteerDraft('');
                        setExpandedRunId(null);
                      } finally {
                        setSteering(false);
                      }
                    }}
                  >
                    <input
                      type="text"
                      value={steerDraft}
                      onChange={(e) => setSteerDraft(e.target.value)}
                      placeholder="Continue with feedback..."
                      className="h-7 min-w-0 flex-1 rounded-lg border border-[var(--border-color)] bg-[var(--bg-panel)] px-2 text-[11px] text-[var(--text-primary)] placeholder:text-[var(--text-muted)] outline-none focus:border-[var(--border-focus)]"
                    />
                    <button
                      type="submit"
                      disabled={steering || !steerDraft.trim()}
                      className="h-7 shrink-0 rounded-lg border border-[var(--border-color)] bg-[var(--bg-panel-subtle)] px-2 text-[11px] font-medium text-[var(--text-primary)] transition-colors hover:bg-[var(--bg-hover)] disabled:opacity-50"
                    >
                      {steering ? '…' : 'Steer'}
                    </button>
                  </form>
                )}

                <div className="mt-3 flex items-center justify-between gap-2 border-t border-[var(--border-color)] pt-2.5">
                  <button
                    type="button"
                    className="rounded-lg border border-[var(--border-color)] bg-[var(--bg-panel-subtle)] px-2.5 py-1 text-xs font-medium text-[var(--text-primary)] transition-colors hover:bg-[var(--bg-hover)]"
                    onClick={() => {
                      onSelectActivity(item);
                      setExpandedRunId(null);
                    }}
                  >
                    Open chat
                  </button>
                  {isRunning && (
                    <button
                      type="button"
                      className="inline-flex items-center gap-1 rounded-lg border border-[var(--border-color)] bg-transparent px-2.5 py-1 text-xs font-medium text-[var(--text-secondary)] transition-colors hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)]"
                      onClick={() => {
                        onStopActivity(item.runId);
                        setExpandedRunId(null);
                      }}
                    >
                      <SquareIcon size={10} fill="currentColor" />
                      <span>Stop</span>
                    </button>
                  )}
                </div>
              </div>
            )}
          </div>
        );
      })}

      {overflow.length > 0 && (
        <div className="relative">
          <button
            type="button"
            className="flex h-[30px] items-center rounded-full border border-[var(--border-color)] bg-[var(--bg-body)] px-2.5 text-xs text-[var(--text-secondary)] transition-colors hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)]"
            onClick={() => {
              setExpandedRunId(null);
              setOverflowOpen((v) => !v);
            }}
          >
            +{overflow.length}
          </button>
          {overflowOpen && (
            <div className="absolute left-0 top-[calc(100%+8px)] z-[9999] flex max-h-[260px] w-[280px] flex-col gap-1 overflow-y-auto rounded-2xl border border-[var(--border-color)] bg-[var(--bg-body)] p-2 shadow-2xl">
              {overflow.map((item) => {
                const overflowTime = formatTaskTimestamp(item.updatedAt, now);
                return (
                  <button
                    key={item.runId}
                    type="button"
                    className="flex items-center justify-between gap-2 rounded-lg px-2.5 py-1.5 text-left text-xs text-[var(--text-primary)] transition-colors hover:bg-[var(--bg-hover)]"
                    onClick={() => {
                      onSelectActivity(item);
                      setOverflowOpen(false);
                    }}
                  >
                    <span className="truncate">{item.title}</span>
                    <span
                      className="shrink-0 text-[11px] tabular-nums text-[var(--text-muted)]"
                      title={overflowTime?.exact}
                    >
                      {getStageLabel(item)}
                      {overflowTime ? ` · ${overflowTime.relative}` : ''}
                    </span>
                  </button>
                );
              })}
              {onOpenMissionControl && (
                <button
                  type="button"
                  className="mt-1 border-t border-[var(--border-color)] px-2.5 pt-2 pb-1 text-left text-[11px] font-medium text-[var(--text-secondary)] transition-colors hover:text-[var(--text-primary)] cursor-pointer"
                  onClick={() => {
                    setOverflowOpen(false);
                    onOpenMissionControl();
                  }}
                >
                  Open Mission Control →
                </button>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

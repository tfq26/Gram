import React, { useMemo, useState } from 'react';
import {
  CheckIcon,
  ClipboardListIcon,
  CopyIcon,
  Edit3Icon,
  PlayIcon,
  SaveIcon,
  XIcon,
} from 'lucide-react';
import { Button } from '../../components/ui/button';
import { MessageResponse } from '../../components/ai-elements/Message';

export interface PlanArtifactCardProps {
  markdown: string;
  onChange: (nextMarkdown: string) => void;
  onSave?: (markdown: string) => Promise<void> | void;
  onApproveAndBuild: (opts: { markdown: string; isolateWorktree: boolean }) => void;
  onDismiss?: () => void;
  variant?: 'inline' | 'stage';
}

function parsePlanSections(markdown: string): {
  requirements: string;
  design: string;
  tasks: string;
  taskItems: Array<{ label: string; checked: boolean }>;
} {
  const extract = (heading: string) => {
    const re = new RegExp(`##\\s+${heading}\\b[\\s\\S]*?(?=\\n##\\s+|$)`, 'i');
    const match = markdown.match(re);
    return match ? match[0].replace(new RegExp(`^##\\s+${heading}[^\\n]*\\n?`, 'i'), '').trim() : '';
  };
  const requirements = extract('Requirements');
  const design = extract('Design');
  const tasks = extract('Tasks');
  const taskSource = tasks || markdown;
  const taskItems = taskSource
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => /^(?:[-*]|\d+\.)\s+/.test(line))
    .map((line) => {
      const checked = /^[-*]\s+\[[xX]\]/.test(line);
      const label = line.replace(/^(?:[-*]\s+\[[ xX]\]|[-*]|\d+\.)\s+/, '').trim();
      return { label, checked };
    });
  return { requirements, design, tasks, taskItems };
}

export function togglePlanTaskCheckbox(markdown: string, taskIndex: number): string {
  const lines = markdown.split('\n');
  let inTasks = false;
  let hasTasksHeading = /^##\s+Tasks\b/im.test(markdown);
  let seen = 0;

  for (let i = 0; i < lines.length; i++) {
    const trimmed = lines[i]!.trim();
    if (/^##\s+/.test(trimmed)) {
      inTasks = /^##\s+Tasks\b/i.test(trimmed);
      continue;
    }
    if (hasTasksHeading && !inTasks) continue;
    if (/^(?:[-*]|\d+\.)\s+/.test(trimmed)) {
      if (seen === taskIndex) {
        const line = lines[i]!;
        if (/^(\s*[-*]\s+)\[x\]\s+/i.test(line)) {
          lines[i] = line.replace(/^(\s*[-*]\s+)\[x\]\s+/i, '$1[ ] ');
        } else if (/^(\s*[-*]\s+)\[ \]\s+/.test(line)) {
          lines[i] = line.replace(/^(\s*[-*]\s+)\[ \]\s+/, '$1[x] ');
        } else if (/^(\s*(?:[-*]|\d+\.)\s+)/.test(line)) {
          lines[i] = line.replace(/^(\s*(?:[-*]|\d+\.)\s+)/, '$1[x] ');
        }
        break;
      }
      seen += 1;
    }
  }
  return lines.join('\n');
}

export function PlanArtifactCard({
  markdown,
  onChange,
  onSave,
  onApproveAndBuild,
  onDismiss,
  variant = 'inline',
}: PlanArtifactCardProps) {
  const [tab, setTab] = useState<'requirements' | 'design' | 'tasks'>('requirements');
  const [editing, setEditing] = useState(false);
  const [isolateWorktree, setIsolateWorktree] = useState(false);
  const [saving, setSaving] = useState(false);

  const parsed = useMemo(() => parsePlanSections(markdown), [markdown]);

  const activeBody =
    tab === 'requirements'
      ? parsed.requirements || markdown
      : tab === 'design'
        ? parsed.design || 'No `## Design` section yet. Switch to Edit to add architectural notes.'
        : parsed.tasks || 'No `## Tasks` section yet.';

  return (
    <div
      className={
        variant === 'stage'
          ? 'mx-auto flex h-full w-full max-w-4xl flex-col rounded-2xl border border-[var(--border-color)] bg-[var(--bg-panel)] overflow-hidden'
          : 'mx-auto mb-3 flex w-full max-w-[780px] flex-col rounded-2xl border border-[var(--border-color)] bg-[var(--bg-panel)] overflow-hidden shadow-lg'
      }
    >
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-[var(--border-color)] px-4 py-2.5">
        <div className="flex items-center gap-2">
          <ClipboardListIcon size={14} className="text-[var(--text-secondary)]" />
          <span className="text-xs font-semibold text-[var(--text-primary)]">Spec &amp; Plan</span>
          <div className="ml-2 inline-flex items-center gap-1 rounded-lg border border-[var(--border-color)] bg-[var(--bg-body)] p-0.5">
            {(
              [
                { id: 'requirements', label: 'Requirements' },
                { id: 'design', label: 'Design' },
                { id: 'tasks', label: `Tasks${parsed.taskItems.length ? ` (${parsed.taskItems.length})` : ''}` },
              ] as const
            ).map((item) => (
              <button
                key={item.id}
                type="button"
                onClick={() => {
                  setEditing(false);
                  setTab(item.id);
                }}
                className={`rounded-md px-2.5 py-1 text-[11px] font-medium transition-colors cursor-pointer ${
                  tab === item.id
                    ? 'bg-[var(--bg-hover)] text-[var(--text-primary)]'
                    : 'text-[var(--text-muted)] hover:text-[var(--text-primary)]'
                }`}
              >
                {item.label}
              </button>
            ))}
          </div>
        </div>

        <div className="flex items-center gap-1.5">
          <button
            type="button"
            onClick={() => void navigator.clipboard?.writeText(markdown)}
            className="inline-flex items-center gap-1 rounded-md border border-[var(--border-color)] px-2 py-1 text-[11px] text-[var(--text-secondary)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
          >
            <CopyIcon size={11} />
            <span>Copy</span>
          </button>
          <button
            type="button"
            onClick={() => setEditing((v) => !v)}
            className="inline-flex items-center gap-1 rounded-md border border-[var(--border-color)] px-2 py-1 text-[11px] text-[var(--text-secondary)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
          >
            <Edit3Icon size={11} />
            <span>{editing ? 'Preview' : 'Edit'}</span>
          </button>
          {onSave && (
            <button
              type="button"
              disabled={saving}
              onClick={async () => {
                setSaving(true);
                try {
                  await onSave(markdown);
                } finally {
                  setSaving(false);
                }
              }}
              className="inline-flex items-center gap-1 rounded-md border border-[var(--border-color)] px-2 py-1 text-[11px] text-[var(--text-secondary)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
            >
              <SaveIcon size={11} />
              <span>{saving ? 'Saved' : 'Save'}</span>
            </button>
          )}
          {onDismiss && (
            <button
              type="button"
              aria-label="Collapse plan"
              onClick={onDismiss}
              className="inline-flex h-6 w-6 items-center justify-center rounded-md text-[var(--text-muted)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
            >
              <XIcon size={12} />
            </button>
          )}
        </div>
      </div>

      <div className={variant === 'stage' ? 'flex-1 overflow-y-auto p-5' : 'max-h-[420px] overflow-y-auto px-4 py-3'}>
        {editing ? (
          <textarea
            value={markdown}
            onChange={(e) => onChange(e.target.value)}
            rows={variant === 'stage' ? 18 : 11}
            placeholder={'## Requirements\n- WHEN ...\n\n### Non-Goals (Deferred)\n- ...\n\n## Design\n- ...\n\n## Tasks\n1. ...'}
            className="w-full resize-y rounded-lg border border-[var(--border-color)] bg-[var(--bg-body)] p-3 font-mono text-xs leading-relaxed text-[var(--text-primary)] outline-none focus:border-[var(--border-focus)]"
          />
        ) : tab === 'tasks' && parsed.taskItems.length > 0 ? (
          <div className="flex flex-col gap-1.5">
            {parsed.taskItems.map((task, idx) => (
              <button
                key={`${idx}-${task.label}`}
                type="button"
                onClick={() => onChange(togglePlanTaskCheckbox(markdown, idx))}
                className="flex w-full items-start gap-2.5 rounded-lg border border-[var(--border-color)] bg-[var(--bg-body)] px-3 py-2 text-left text-xs text-[var(--text-primary)] transition-colors hover:bg-[var(--bg-hover)] cursor-pointer"
              >
                <span
                  className={`mt-0.5 inline-flex h-4 w-4 shrink-0 items-center justify-center rounded border text-[10px] ${
                    task.checked
                      ? 'border-[var(--accent)] bg-[var(--accent-light)] text-[var(--accent-text)]'
                      : 'border-[var(--border-color)] text-[var(--text-muted)]'
                  }`}
                >
                  {task.checked ? <CheckIcon size={10} /> : idx + 1}
                </span>
                <span className={`leading-snug ${task.checked ? 'line-through text-[var(--text-muted)]' : ''}`}>
                  {task.label}
                </span>
              </button>
            ))}
          </div>
        ) : (
          <div className="text-xs leading-relaxed text-[var(--text-primary)]">
            <MessageResponse isStreaming={false}>
              {activeBody || 'Ask Gram to draft a plan, or click Edit to write your requirements, design, and tasks.'}
            </MessageResponse>
          </div>
        )}
      </div>

      <div className="flex flex-wrap items-center justify-between gap-2 border-t border-[var(--border-color)] bg-[var(--bg-body)] px-4 py-2.5">
        <label className="inline-flex items-center gap-2 text-[11px] text-[var(--text-secondary)] cursor-pointer select-none">
          <input
            type="checkbox"
            checked={isolateWorktree}
            onChange={(e) => setIsolateWorktree(e.target.checked)}
            className="h-3.5 w-3.5 rounded border-[var(--border-color)]"
          />
          <span>Isolate execution in Git worktree</span>
        </label>

        <Button
          type="button"
          size="sm"
          className="h-7 gap-1.5 rounded-lg px-3 text-xs font-medium"
          onClick={() => onApproveAndBuild({ markdown, isolateWorktree })}
        >
          <PlayIcon size={11} />
          <span>Approve &amp; Build</span>
        </Button>
      </div>
    </div>
  );
}

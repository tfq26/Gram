import React, { useMemo, useState } from 'react';
import {
  CheckIcon,
  CopyIcon,
  Edit3Icon,
  FileTextIcon,
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

export interface DocumentSection {
  id: string;
  title: string;
  body: string;
  isTasks: boolean;
  taskItems: Array<{ label: string; checked: boolean }>;
}

function parseTaskLines(text: string): Array<{ label: string; checked: boolean }> {
  return text
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => /^(?:[-*]|\d+\.)\s+/.test(line))
    .map((line) => {
      const checked = /^(?:[-*]|\d+\.)\s+\[[xX]\]/.test(line);
      const label = line.replace(/^(?:[-*]|\d+\.)\s+\[[ xX]\]\s+|^(?:[-*]|\d+\.)\s+/, '').trim();
      return { label, checked };
    });
}

export function parseDocumentSections(markdown: string): DocumentSection[] {
  const lines = markdown.split('\n');
  const sections: Array<{ title: string; lines: string[] }> = [];
  let current: { title: string; lines: string[] } | null = null;

  for (const line of lines) {
    const headingMatch = line.match(/^##\s+(.+?)\s*$/);
    if (headingMatch) {
      if (current) sections.push(current);
      current = { title: headingMatch[1]!.trim(), lines: [] };
    } else {
      if (!current) {
        current = { title: '', lines: [] };
      }
      current.lines.push(line);
    }
  }
  if (current) sections.push(current);

  return sections
    .map((sec, idx) => {
      const body = sec.lines.join('\n').trim();
      const isTasks = /^tasks?\b/i.test(sec.title);
      const taskItems = isTasks ? parseTaskLines(body) : [];
      return {
        id: `plan-sec-${idx}`,
        title: sec.title,
        body,
        isTasks,
        taskItems,
      };
    })
    .filter((sec) => sec.title || sec.body);
}

export function togglePlanTaskCheckbox(markdown: string, taskIndex: number): string {
  const lines = markdown.split('\n');
  let inTasks = false;
  const hasTasksHeading = /^##\s+Tasks?\b/im.test(markdown);
  let seen = 0;

  for (let i = 0; i < lines.length; i++) {
    const trimmed = lines[i]!.trim();
    if (/^##\s+/.test(trimmed)) {
      inTasks = /^##\s+Tasks?\b/i.test(trimmed);
      continue;
    }
    if (hasTasksHeading && !inTasks) continue;
    if (/^(?:[-*]|\d+\.)\s+/.test(trimmed)) {
      if (seen === taskIndex) {
        const line = lines[i]!;
        if (/^(\s*(?:[-*]|\d+\.)\s+)\[x\]\s+/i.test(line)) {
          lines[i] = line.replace(/^(\s*(?:[-*]|\d+\.)\s+)\[x\]\s+/i, '$1[ ] ');
        } else if (/^(\s*(?:[-*]|\d+\.)\s+)\[ \]\s+/.test(line)) {
          lines[i] = line.replace(/^(\s*(?:[-*]|\d+\.)\s+)\[ \]\s+/, '$1[x] ');
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
  const [editing, setEditing] = useState(false);
  const [isolateWorktree, setIsolateWorktree] = useState(false);
  const [saving, setSaving] = useState(false);

  const sections = useMemo(() => parseDocumentSections(markdown), [markdown]);
  const namedSections = sections.filter((s) => Boolean(s.title));

  return (
    <div
      className={
        variant === 'stage'
          ? 'mx-auto flex h-full w-full max-w-3xl flex-col px-2'
          : 'mx-auto mb-4 flex w-full max-w-[760px] flex-col'
      }
    >
      {/* Minimal document utility bar */}
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-[var(--border-color)]/45 pb-3">
        <div className="flex flex-wrap items-center gap-2">
          <span className="inline-flex items-center gap-1.5 text-xs font-medium text-[var(--text-secondary)]">
            <FileTextIcon size={13} className="text-[var(--text-muted)]" />
            <span>Plan document</span>
          </span>
          {namedSections.length > 1 && !editing && (
            <nav aria-label="Plan sections" className="ml-2 flex flex-wrap items-center gap-1">
              {namedSections.map((sec) => (
                <a
                  key={sec.id}
                  href={`#${sec.id}`}
                  onClick={(e) => {
                    e.preventDefault();
                    document.getElementById(sec.id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
                  }}
                  className="rounded-md px-2 py-0.5 text-[11px] text-[var(--text-muted)] transition-colors hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)]"
                >
                  {sec.title}
                </a>
              ))}
            </nav>
          )}
        </div>

        <div className="flex items-center gap-1">
          <button
            type="button"
            onClick={() => void navigator.clipboard?.writeText(markdown)}
            className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-[11px] text-[var(--text-muted)] transition-colors hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
          >
            <CopyIcon size={11} />
            <span>Copy</span>
          </button>
          <button
            type="button"
            onClick={() => setEditing((v) => !v)}
            className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-[11px] text-[var(--text-muted)] transition-colors hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
          >
            <Edit3Icon size={11} />
            <span>{editing ? 'Done' : 'Edit'}</span>
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
              className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-[11px] text-[var(--text-muted)] transition-colors hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
            >
              <SaveIcon size={11} />
              <span>{saving ? 'Saving...' : 'Save'}</span>
            </button>
          )}
          {onDismiss && (
            <button
              type="button"
              aria-label="Close plan"
              onClick={onDismiss}
              className="inline-flex h-6 w-6 items-center justify-center rounded-md text-[var(--text-muted)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
            >
              <XIcon size={12} />
            </button>
          )}
        </div>
      </div>

      {/* Document body */}
      <div className={variant === 'stage' ? 'flex-1 overflow-y-auto py-5' : 'py-5'}>
        {editing ? (
          <textarea
            value={markdown}
            onChange={(e) => onChange(e.target.value)}
            rows={variant === 'stage' ? 20 : 14}
            placeholder={'## Overview\n...\n\n## Requirements\n...\n\n## Design\n...\n\n## Tasks\n1. [ ] ...'}
            className="w-full resize-y rounded-xl border border-[var(--border-color)]/60 bg-[var(--bg-panel)]/50 p-4 font-mono text-xs leading-relaxed text-[var(--text-primary)] outline-none focus:border-[var(--border-focus)]"
          />
        ) : sections.length === 0 ? (
          <div className="text-sm text-[var(--text-muted)]">
            No plan content yet. Click Edit to write your plan or ask Gram to draft one below.
          </div>
        ) : (
          <div className="flex flex-col gap-6">
            {sections.map((sec, idx) => (
              <section
                key={sec.id}
                id={sec.id}
                className={idx > 0 ? 'border-t border-[var(--border-color)]/35 pt-6' : ''}
              >
                {sec.title && (
                  <h2 className="mb-3 text-base font-semibold tracking-tight text-[var(--text-primary)]">
                    {sec.title}
                  </h2>
                )}
                {sec.isTasks && sec.taskItems.length > 0 ? (
                  <div className="flex flex-col gap-2">
                    {sec.taskItems.map((task, tIdx) => (
                      <button
                        key={`${tIdx}-${task.label}`}
                        type="button"
                        onClick={() => onChange(togglePlanTaskCheckbox(markdown, tIdx))}
                        className="flex w-full items-start gap-2.5 rounded-lg px-2 py-1.5 text-left text-sm text-[var(--text-primary)] transition-colors hover:bg-[var(--bg-hover)]/60 cursor-pointer"
                      >
                        <span
                          className={`mt-0.5 inline-flex h-4 w-4 shrink-0 items-center justify-center rounded border text-[10px] ${
                            task.checked
                              ? 'border-[var(--accent)] bg-[var(--accent-light)] text-[var(--accent-text)]'
                              : 'border-[var(--border-color)] text-[var(--text-muted)]'
                          }`}
                        >
                          {task.checked ? <CheckIcon size={10} /> : tIdx + 1}
                        </span>
                        <span className={`leading-relaxed ${task.checked ? 'line-through text-[var(--text-muted)]' : ''}`}>
                          {task.label}
                        </span>
                      </button>
                    ))}
                  </div>
                ) : (
                  <div className="text-sm leading-relaxed text-[var(--text-primary)]">
                    <MessageResponse isStreaming={false}>
                      {sec.body}
                    </MessageResponse>
                  </div>
                )}
              </section>
            ))}
          </div>
        )}
      </div>

      {/* Document footer */}
      <div className="flex flex-wrap items-center justify-between gap-3 border-t border-[var(--border-color)]/45 pt-4">
        <label className="inline-flex items-center gap-2 text-xs text-[var(--text-secondary)] cursor-pointer select-none">
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
          className="h-8 gap-1.5 rounded-lg px-3.5 text-xs font-medium"
          onClick={() => onApproveAndBuild({ markdown, isolateWorktree })}
        >
          <PlayIcon size={12} />
          <span>Approve &amp; Build</span>
        </Button>
      </div>
    </div>
  );
}

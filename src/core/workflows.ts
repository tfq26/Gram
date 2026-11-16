import fs from 'node:fs';
import path from 'node:path';

export interface WorkflowDefinition {
  id: string;
  title: string;
  description: string;
  triggers: RegExp[];
  allowedToolsHint?: string[];
  instructions: string;
}

const BUILTIN_WORKFLOWS: WorkflowDefinition[] = [
  {
    id: 'debug-failing-tests',
    title: 'Debug Failing Tests',
    description: 'Diagnose test failures, fix the root cause, and re-run verification.',
    triggers: [/\bfailing test/i, /\bfix (?:the )?tests?\b/i, /\btest suite\b/i, /\bdebug.*test/i],
    allowedToolsHint: ['run_tests', 'read_file', 'search_code', 'edit_file', 'validate_project'],
    instructions: [
      '1. Run `run_tests` (or the project test command via `run_command`) first to observe the exact failure and stack trace.',
      '2. Read the failing test and the implementation under test with `read_file`.',
      '3. Fix the root cause with `edit_file` (do not weaken or skip assertions unless the test itself is outdated).',
      '4. Re-run the full relevant test suite and report exact pass/fail counts.',
    ].join('\n'),
  },
  {
    id: 'review-diff',
    title: 'Review Code / Diff',
    description: 'Audit staged or git changes for bugs, security issues, and regressions.',
    triggers: [/\breview (?:my |the )?(?:pr|diff|changes|code)\b/i, /\bcode review\b/i],
    allowedToolsHint: ['git_diff', 'git_status', 'gramfs_diff', 'read_file', 'search_code'],
    instructions: [
      '1. Inspect `git_diff` / `gramfs_diff` and `git_status` to see all modified files.',
      '2. Read surrounding context in each modified file with `read_file` to verify invariants, error handling, and edge cases.',
      '3. Report concrete findings ordered by severity with exact file paths and line numbers, or confirm no issues found.',
    ].join('\n'),
  },
  {
    id: 'build-and-inspect-ui',
    title: 'Build & Visually Inspect UI',
    description: 'Build/launch a web UI, open it in a headless browser, inspect layout, and fix issues.',
    triggers: [/\bvisually inspect\b/i, /\bscreenshot\b/i, /\binspect (?:the )?(?:page|ui|site)\b/i, /\bcheck (?:the )?ui\b/i],
    allowedToolsHint: ['start_process', 'browser_navigate', 'browser_snapshot', 'browser_screenshot', 'browser_close'],
    instructions: [
      '1. Ensure the app builds (`validate_project` / `run_command`) and start the dev server with `start_process` if needed.',
      '2. Open the local URL with `browser_navigate`, inspect the DOM/state with `browser_snapshot`, and capture a screenshot via `browser_screenshot`.',
      '3. Fix any layout overflow, console error, or broken interaction, rebuild, re-verify, and close the browser with `browser_close`.',
    ].join('\n'),
  },
];

/**
 * Loads custom `.gram/workflows/*.md` playbooks from the workspace and merges with built-in workflows.
 */
export function listWorkspaceWorkflows(workspaceRoot: string): WorkflowDefinition[] {
  const workflows = [...BUILTIN_WORKFLOWS];
  const dir = path.join(workspaceRoot, '.gram', 'workflows');
  try {
    if (!fs.existsSync(dir)) return workflows;
    for (const file of fs.readdirSync(dir)) {
      if (!file.endsWith('.md')) continue;
      const fullPath = path.join(dir, file);
      const raw = fs.readFileSync(fullPath, 'utf8').trim();
      if (!raw) continue;
      const id = file.replace(/\.md$/, '');
      const firstHeading = raw.match(/^#\s+(.+)$/m)?.[1]?.trim() || id;
      const keywords = id
        .split(/[-_]+/)
        .map((w) => w.trim())
        .filter((w) => w.length >= 3);
      workflows.push({
        id,
        title: firstHeading,
        description: `Custom workspace workflow (${file})`,
        triggers: keywords.length > 0 ? [new RegExp(keywords.join('.*'), 'i')] : [],
        instructions: raw.slice(0, 2000),
      });
    }
  } catch {}
  return workflows;
}

export function matchWorkflowForPrompt(
  workspaceRoot: string,
  prompt: string
): WorkflowDefinition | null {
  const workflows = listWorkspaceWorkflows(workspaceRoot);
  for (const wf of workflows) {
    if (wf.triggers.some((re) => re.test(prompt))) {
      return wf;
    }
  }
  return null;
}

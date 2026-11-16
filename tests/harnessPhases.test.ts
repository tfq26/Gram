import { describe, expect, test } from 'bun:test';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { WorktreeManager } from '../src/core/worktreeManager';
import { listWorkspaceWorkflows, matchWorkflowForPrompt } from '../src/core/workflows';
import { DEFAULT_HARNESS_EVAL_CASES, evaluateHarnessRun } from '../src/evals/harnessEval';
import { GramAgentRuntime } from '../src/runtime/agentRuntime';

describe('Harness Phases 1–4 (Lifecycle, Evals, Worktrees, Workflows)', () => {
  test('Phase 1: resumeRun preserves durable handoff metadata and records steering feedback', async () => {
    const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'gram-phase1-'));
    const runtime = new GramAgentRuntime(tmpDir);
    try {
      runtime.saveCheckpoint({
        sessionId: 'sess-steer',
        runId: 'run-steer',
        sequence: 1,
        stage: 'paused',
        goal: 'Build landing page',
        modifiedFiles: ['src/App.tsx'],
        lastVerification: {
          command: 'bun run typecheck',
          passed: true,
          summary: '0 errors',
          timestamp: new Date().toISOString(),
        },
        activePlan: [{ id: 'p1', title: 'Add hero section', status: 'pending', dependencies: [] }],
        completedTaskIds: [],
        pinnedFacts: {},
        activeLayerId: 'main',
        timestamp: new Date().toISOString(),
      });

      const ok = await runtime.resumeRun('sess-steer', 'run-steer', {
        feedback: 'Use dark theme and Geist font',
      });
      expect(ok).toBe(true);
      const updated = runtime.getCheckpoint('sess-steer', 'run-steer');
      expect(updated?.stage).toBe('completed');
      expect(updated?.nextAction).toBe('Use dark theme and Geist font');
      expect(updated?.modifiedFiles).toContain('src/App.tsx');
      expect(updated?.lastVerification?.passed).toBe(true);
    } finally {
      runtime.close();
      fs.rmSync(tmpDir, { recursive: true, force: true });
    }
  });

  test('Phase 2: evaluateHarnessRun grades inspect-first, verify-after-edit, and conciseness', () => {
    const evalCase = DEFAULT_HARNESS_EVAL_CASES[1]!;

    const goodRun = evaluateHarnessRun(evalCase, {
      output: 'Fixed `src/math.ts` and verified `bun test` passed (4/4).',
      toolCalls: [
        { name: 'read_file', command: 'read_file({"path":"src/math.ts"})', result: 'ok' },
        { name: 'edit_file', command: 'edit_file({"path":"src/math.ts"})', result: 'ok' },
        { name: 'run_tests', command: 'run_tests({})', result: '{"passed":true}' },
      ],
    });
    expect(goodRun.passed).toBe(true);
    expect(goodRun.score).toBe(100);

    const badRun = evaluateHarnessRun(evalCase, {
      output: '```ts\n' + Array.from({ length: 22 }, (_, i) => `const x${i} = ${i};`).join('\n') + '\n```',
      toolCalls: [{ name: 'write_file', command: 'write_file({"path":"src/math.ts"})', result: 'ok' }],
    });
    expect(badRun.passed).toBe(false);
    expect(badRun.score).toBe(0);
  });

  test('Phase 3: WorktreeManager isolates task edits from the main Git checkout', () => {
    const repoDir = fs.mkdtempSync(path.join(os.tmpdir(), 'gram-wt-'));
    try {
      execFileSync('git', ['init'], { cwd: repoDir, stdio: 'ignore' });
      execFileSync('git', ['config', 'user.email', 'test@gram.local'], { cwd: repoDir, stdio: 'ignore' });
      execFileSync('git', ['config', 'user.name', 'Gram Test'], { cwd: repoDir, stdio: 'ignore' });
      fs.writeFileSync(path.join(repoDir, 'README.md'), '# Repo\n', 'utf8');
      execFileSync('git', ['add', '.'], { cwd: repoDir, stdio: 'ignore' });
      execFileSync('git', ['commit', '-m', 'init'], { cwd: repoDir, stdio: 'ignore' });

      const wt = WorktreeManager.createTaskWorktree(repoDir, 'run-isolated-1');
      expect(wt).not.toBeNull();
      expect(fs.existsSync(wt!.worktreePath)).toBe(true);

      fs.writeFileSync(path.join(wt!.worktreePath, 'README.md'), '# Isolated Edit\n', 'utf8');
      expect(fs.readFileSync(path.join(repoDir, 'README.md'), 'utf8')).toBe('# Repo\n');
      expect(WorktreeManager.getWorktreeDiff(wt!.worktreePath)).toContain('# Isolated Edit');

      expect(WorktreeManager.removeTaskWorktree(repoDir, wt!.worktreePath)).toBe(true);
      expect(fs.existsSync(wt!.worktreePath)).toBe(false);
    } finally {
      fs.rmSync(repoDir, { recursive: true, force: true });
    }
  });

  test('Phase 4: Workflows match built-in and custom .gram/workflows/*.md playbooks', () => {
    const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'gram-wf-'));
    try {
      const builtin = matchWorkflowForPrompt(tmpDir, 'Can you debug the failing tests in auth?');
      expect(builtin?.id).toBe('debug-failing-tests');

      const wfDir = path.join(tmpDir, '.gram', 'workflows');
      fs.mkdirSync(wfDir, { recursive: true });
      fs.writeFileSync(
        path.join(wfDir, 'cloudflare-deploy.md'),
        '# Cloudflare Deploy\nRun `bun run build:studio` and check `wrangler`.',
        'utf8'
      );

      const all = listWorkspaceWorkflows(tmpDir);
      expect(all.some((w) => w.id === 'cloudflare-deploy')).toBe(true);
      const custom = matchWorkflowForPrompt(tmpDir, 'Run the cloudflare deploy workflow');
      expect(custom?.id).toBe('cloudflare-deploy');
    } finally {
      fs.rmSync(tmpDir, { recursive: true, force: true });
    }
  });
});

import { describe, it, expect, beforeEach, afterEach } from 'bun:test';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { GramAgentRuntime } from '../src/runtime/agentRuntime';

describe('Gram Agent Runtime (Sync, Async & Local Hibernation Checkpoints)', () => {
  let tempDir: string;
  let runtime: GramAgentRuntime;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'agent-runtime-test-'));
    runtime = new GramAgentRuntime(tempDir);
  });

  afterEach(() => {
    runtime.close();
    fs.rmSync(tempDir, { recursive: true, force: true });
  });

  it('executes a synchronous multi-step plan with persistent checkpoints', async () => {
    const plan = [
      { id: 'step-1', title: 'Verify dependencies', status: 'pending' as const, dependencies: [] },
      { id: 'step-2', title: 'Build components', status: 'pending' as const, dependencies: ['step-1'] },
    ];

    const result = await runtime.executeRun({
      sessionId: 'sess-sync-1',
      runId: 'run-1',
      prompt: 'Build application pipeline',
      plan,
    });

    expect(result.completed).toBe(true);
    expect(result.checkpoint.stage).toBe('completed');
    expect(result.checkpoint.completedTaskIds).toContain('step-1');
    expect(result.checkpoint.completedTaskIds).toContain('step-2');

    // Verify checkpoint is stored in SQLite
    const retrieved = runtime.getCheckpoint('sess-sync-1', 'run-1');
    expect(retrieved).not.toBeNull();
    expect(retrieved?.stage).toBe('completed');
  });

  it('runs an asynchronous worker in an isolated GramFS layer', async () => {
    const result = await runtime.executeRun({
      sessionId: 'sess-async-1',
      runId: 'run-async',
      prompt: 'Refactor background task',
      isAsync: true,
    });

    expect(result.completed).toBe(true);
    expect(result.checkpoint.activeLayerId).toBe('async-run-async');
  });

  it('resumes an interrupted run from its saved checkpoint state', async () => {
    // Manually seed an interrupted checkpoint
    runtime.saveCheckpoint({
      sessionId: 'sess-interrupted',
      runId: 'run-interrupted',
      sequence: 1,
      stage: 'executing',
      activePlan: [
        { id: 't1', title: 'Done task', status: 'completed', dependencies: [] },
        { id: 't2', title: 'Unfinished task', status: 'pending', dependencies: [] },
      ],
      completedTaskIds: ['t1'],
      pinnedFacts: { lang: 'typescript' },
      activeLayerId: 'main',
      timestamp: new Date().toISOString(),
    });

    const resumed = await runtime.resumeRun('sess-interrupted', 'run-interrupted');
    expect(resumed).toBe(true);

    const updated = runtime.getCheckpoint('sess-interrupted', 'run-interrupted');
    expect(updated?.stage).toBe('completed');
    expect(updated?.completedTaskIds).toContain('t2');
  });

  it('tracks dirty state and synchronizes chats and task checkpoints across devices', async () => {
    const tempDirB = fs.mkdtempSync(path.join(os.tmpdir(), 'agent-runtime-b-'));
    const deviceB = new GramAgentRuntime(tempDirB);

    try {
      // Device A creates a chat and runs a task
      runtime.saveChatSession({
        sessionId: 'sess-sync-xdev',
        title: 'Cross-Device Sync Chat',
        projectId: 'local',
        projectLabel: 'Local workspace',
        modelId: 'auto',
        messages: [{ id: 'm1', role: 'user', content: 'Hello from Device A' }],
      });
      await runtime.executeRun({
        sessionId: 'sess-sync-xdev',
        runId: 'run-xdev-1',
        prompt: 'Inspect workspace sync',
      });

      const dirtyA = runtime.getDirtySyncPayload();
      expect(dirtyA.chats.length).toBe(1);
      expect(dirtyA.tasks.length).toBe(1);

      // Cloud returns delta with sequence 42 and Device A acknowledges its pushed rows
      runtime.applyRemoteSyncDelta({
        latestSeq: 42,
        pushedChatIds: dirtyA.chats.map((c) => c.sessionId),
        pushedRunKeys: dirtyA.tasks.map((t) => ({ sessionId: t.sessionId, runId: t.runId })),
      });
      const afterAckA = runtime.getDirtySyncPayload();
      expect(afterAckA.chats.length).toBe(0);
      expect(afterAckA.tasks.length).toBe(0);
      expect(afterAckA.sinceSeq).toBe(42);

      // Device B pulls that delta from the cloud
      deviceB.applyRemoteSyncDelta({
        latestSeq: 42,
        chats: dirtyA.chats,
        tasks: dirtyA.tasks,
      });

      const chatOnB = deviceB.getChatSession('sess-sync-xdev');
      expect(chatOnB?.title).toBe('Cross-Device Sync Chat');
      expect(chatOnB?.messages.length).toBe(1);
      const taskOnB = deviceB.getCheckpoint('sess-sync-xdev', 'run-xdev-1');
      expect(taskOnB?.stage).toBe('completed');
      expect(deviceB.getDirtySyncPayload().sinceSeq).toBe(42);
    } finally {
      deviceB.close();
      fs.rmSync(tempDirB, { recursive: true, force: true });
    }
  });

  it('runs a queued Git commit only after explicit approval', async () => {
    const git = (args: string[]) => Bun.spawnSync(['git', ...args], { cwd: tempDir, stdout: 'pipe', stderr: 'pipe' });
    expect(git(['init', '-q']).exitCode).toBe(0);
    expect(git(['config', 'user.name', 'Gram Test']).exitCode).toBe(0);
    expect(git(['config', 'user.email', 'gram-test@example.invalid']).exitCode).toBe(0);
    fs.writeFileSync(path.join(tempDir, 'approved.txt'), 'reviewed');
    expect(git(['add', 'approved.txt']).exitCode).toBe(0);
    const checkpoint = {
      sessionId: 'sess-git-approval', runId: 'run-git-approval', sequence: 1, stage: 'paused' as const,
      activePlan: [], completedTaskIds: [], pinnedFacts: {}, activeLayerId: 'main', timestamp: new Date().toISOString(),
      pendingApproval: {
        id: 'apr-git', title: 'Confirmation required', command: 'git commit -m "Approved test commit"',
        action: { type: 'git_commit' as const, message: 'Approved test commit' },
      },
    };
    runtime.saveCheckpoint(checkpoint);
    expect(git(['rev-parse', '--verify', 'HEAD']).exitCode).not.toBe(0);
    const approved = await runtime.respondApproval(checkpoint.sessionId, checkpoint.runId, true);
    expect(approved?.stage).toBe('completed');
    expect(git(['log', '-1', '--format=%s']).stdout.toString().trim()).toBe('Approved test commit');
  });

  it('persists workspace rules separately and indexes staged overlay symbols', () => {
    const first = runtime.rememberWorkspaceRule('Use focused tests.');
    const duplicate = runtime.rememberWorkspaceRule('Use focused tests.');
    expect(first.created).toBe(true);
    expect(duplicate.created).toBe(false);
    expect(runtime.listWorkspaceRules()).toEqual(['Use focused tests.']);

    fs.writeFileSync(path.join(tempDir, 'host.ts'), 'export function hostSymbol() { return 1; }');
    runtime.gramfs.writeFile('staged.ts', 'export function stagedSymbol() { return 2; }', 'main');
    runtime.indexWorkspaceSymbols('main');
    expect(runtime.graph.listSymbols().some((symbol) => symbol.name === 'hostSymbol')).toBe(true);
    expect(runtime.graph.listSymbols().some((symbol) => symbol.name === 'stagedSymbol')).toBe(true);
  });
});

import { describe, it, expect, afterEach } from 'bun:test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { GramAgentRuntime } from '../src/runtime/agentRuntime';
import { ManagedProcessManager } from '../src/runtime/processManager';
import { executeAgentTool } from '../src/runtime/agentTools';
import { GramFS } from '../src/core/gramfs';
import {
  clearUnavailableModels,
  markModelUnavailable,
  selectOptimalModel,
} from '../src/providers/router';

describe('Project workspace binding, managed processes, and model fallback', () => {
  const tempDirs: string[] = [];

  afterEach(() => {
    clearUnavailableModels();
    while (tempDirs.length > 0) {
      const dir = tempDirs.pop()!;
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });

  it('fails loudly when a named project folder is missing or moved instead of falling back to host workspace', async () => {
    const hostDir = fs.mkdtempSync(path.join(os.tmpdir(), 'gram-host-'));
    const missingDir = path.join(os.tmpdir(), `gram-missing-${Date.now()}`);
    tempDirs.push(hostDir);

    const runtime = new GramAgentRuntime(hostDir);
    try {
      runtime.saveProject({
        id: 'proj-moved',
        label: 'MovedProject',
        workspacePath: missingDir,
        instructions: '',
      });

      const events: any[] = [];
      const res = await runtime.executeRun({
        sessionId: 'sess-missing',
        prompt: 'list files',
        projectId: 'proj-moved',
        projectLabel: 'MovedProject',
        onEvent: (evt) => events.push(evt),
      });

      expect(res.completed).toBe(false);
      expect(res.checkpoint.stage).toBe('failed');
      expect(res.output).toContain('unavailable or has been moved');
      expect(events.some((e) => e.type === 'error' && String(e.error).includes('unavailable or has been moved'))).toBe(
        true
      );
    } finally {
      runtime.close();
    }
  });

  it('starts, logs, and stops a managed background process and enforces approval when requested', async () => {
    const ws = fs.mkdtempSync(path.join(os.tmpdir(), 'gram-proc-'));
    tempDirs.push(ws);
    const gramfs = new GramFS({ workspaceRoot: ws });

    try {
      const approvalRes = await executeAgentTool({
        name: 'start_process',
        input: { command: 'node -e "setInterval(() => {}, 1000)"' },
        workspaceRoot: ws,
        gramfs,
        layerId: 'main',
        requireApproval: true,
      });
      expect(approvalRes.requiresApproval).toBe(true);
      expect(approvalRes.approvalAction).toEqual({
        type: 'run_command',
        command: 'node -e "setInterval(() => {}, 1000)"',
      });

      const mgr = new ManagedProcessManager();
      const started = await mgr.start({
        command: `bun -e 'console.log("service-ready"); setInterval(() => {}, 1000)'`,
        cwd: ws,
        label: 'demo-service',
        waitMs: 800,
      });
      expect(started.process.status).toBe('running');
      expect(started.initialOutput).toContain('service-ready');
      expect(mgr.list(ws).length).toBe(1);

      const logs = mgr.getLogs(started.process.id, 20);
      expect(logs?.logs).toContain('service-ready');

      const stopped = await mgr.stop(started.process.id);
      expect(stopped?.status).toBe('stopped');
    } finally {
      gramfs.close();
    }
  });

  it('skips models marked unavailable in selectOptimalModel', () => {
    const first = selectOptimalModel({ taskComplexity: 'moderate', priority: 'balanced' });
    markModelUnavailable(first.id);
    const second = selectOptimalModel({ taskComplexity: 'moderate', priority: 'balanced' });
    expect(second.id).not.toBe(first.id);
  });
});

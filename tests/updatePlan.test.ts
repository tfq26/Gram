import { describe, expect, test } from 'bun:test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { GramFS } from '../src/core/gramfs';
import { executeAgentTool } from '../src/runtime/agentTools';

describe('update_plan tool', () => {
  test('writes session plan markdown to .gram/plans/<sessionId>.md without touching source files', async () => {
    const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'gram-update-plan-'));
    try {
      const gramfs = new GramFS({ workspaceRoot: tmpDir });
      const res = await executeAgentTool({
        name: 'update_plan',
        input: {
          markdown: '## Requirements\n- WHEN user submits THEN system plans.\n\n## Tasks\n- [ ] Build UI',
          summary: 'Initial plan',
        },
        workspaceRoot: tmpDir,
        gramfs,
        layerId: 'main',
        requireApproval: false,
        sessionId: 'session-plan-123',
      });
      expect(res.isError).toBeUndefined();
      expect(res.output).toContain('Updated Plan document');

      const saved = fs.readFileSync(path.join(tmpDir, '.gram', 'plans', 'session-plan-123.md'), 'utf8');
      expect(saved).toContain('## Requirements');
      expect(saved).toContain('## Tasks');
    } finally {
      fs.rmSync(tmpDir, { recursive: true, force: true });
    }
  });
});

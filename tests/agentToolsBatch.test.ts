import { describe, expect, test } from 'bun:test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { GramFS } from '../src/core/gramfs';
import { executeAgentTool } from '../src/runtime/agentTools';

describe('Batch and overlay-aware agentTools', () => {
  test('supports batch write_file, read_file, delete_file, overlay-aware list_files, and search_code', async () => {
    const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'gram-tools-batch-'));
    try {
      fs.writeFileSync(path.join(tmpDir, 'keep.ts'), 'export const keep = 1;\n', 'utf8');
      fs.writeFileSync(path.join(tmpDir, 'old.ts'), 'export const oldVal = 2;\n', 'utf8');

      const gramfs = new GramFS({ workspaceRoot: tmpDir });
      const layerId = 'test-layer';

      const writeRes = await executeAgentTool({
        name: 'write_file',
        input: {
          files: [
            { path: 'a.ts', content: 'export const alpha = 10;\n' },
            { path: 'b.ts', content: 'export const beta = 20;\n' },
          ],
        },
        workspaceRoot: tmpDir,
        gramfs,
        layerId,
        requireApproval: false,
      });
      expect(writeRes.isError).toBeUndefined();
      expect(writeRes.output).toContain('Atomically staged 2 file(s)');

      const readRes = await executeAgentTool({
        name: 'read_file',
        input: { paths: ['a.ts', 'b.ts'] },
        workspaceRoot: tmpDir,
        gramfs,
        layerId,
        requireApproval: false,
      });
      expect(readRes.isError).toBeUndefined();
      expect(readRes.output).toContain('=== a.ts ===');
      expect(readRes.output).toContain('alpha = 10');
      expect(readRes.output).toContain('=== b.ts ===');
      expect(readRes.output).toContain('beta = 20');

      const delRes = await executeAgentTool({
        name: 'delete_file',
        input: { paths: ['old.ts', 'b.ts'] },
        workspaceRoot: tmpDir,
        gramfs,
        layerId,
        requireApproval: false,
      });
      expect(delRes.isError).toBeUndefined();
      expect(delRes.output).toContain('Staged deletion of 2 files');

      const listRes = await executeAgentTool({
        name: 'list_files',
        input: { dir: '.' },
        workspaceRoot: tmpDir,
        gramfs,
        layerId,
        requireApproval: false,
      });
      expect(listRes.output).toContain('keep.ts');
      expect(listRes.output).toContain('a.ts');
      expect(listRes.output).not.toContain('old.ts');
      expect(listRes.output).not.toContain('b.ts');

      const searchRes = await executeAgentTool({
        name: 'search_code',
        input: { query: 'alpha' },
        workspaceRoot: tmpDir,
        gramfs,
        layerId,
        requireApproval: false,
      });
      expect(searchRes.isError).toBeUndefined();
      expect(searchRes.output).toContain('a.ts:1:');
    } finally {
      fs.rmSync(tmpDir, { recursive: true, force: true });
    }
  });
});

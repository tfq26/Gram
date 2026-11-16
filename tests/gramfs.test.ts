import { describe, it, expect, beforeEach, afterEach } from 'bun:test';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { GramFS } from '../src/core/gramfs';

describe('GramFS CoW Virtual Filesystem', () => {
  let tempDir: string;
  let gramfs: GramFS;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'gramfs-test-'));
    fs.writeFileSync(path.join(tempDir, 'hello.txt'), 'Hello Host World');
    gramfs = new GramFS({ workspaceRoot: tempDir });
  });

  afterEach(() => {
    gramfs.close();
    fs.rmSync(tempDir, { recursive: true, force: true });
  });

  it('reads host file when overlay is empty', () => {
    const content = gramfs.readFile('hello.txt');
    expect(content).toBe('Hello Host World');
  });

  it('writes to overlay without modifying the host file', () => {
    gramfs.writeFile('hello.txt', 'Modified in Overlay', 'main');

    // Overlay sees new content
    expect(gramfs.readFile('hello.txt', 'main')).toBe('Modified in Overlay');

    // Host file is untouched
    const hostContent = fs.readFileSync(path.join(tempDir, 'hello.txt'), 'utf8');
    expect(hostContent).toBe('Hello Host World');
  });

  it('forks overlay into an isolated branch for an async subagent in 0ms', () => {
    gramfs.writeFile('hello.txt', 'Agent 1 Draft', 'main');
    gramfs.forkOverlay('main', 'worker-hypothesis-1');

    // Worker modifies file in its own layer
    gramfs.writeFile('hello.txt', 'Worker 1 Alternate Fix', 'worker-hypothesis-1');

    // Main and worker remain completely isolated
    expect(gramfs.readFile('hello.txt', 'main')).toBe('Agent 1 Draft');
    expect(gramfs.readFile('hello.txt', 'worker-hypothesis-1')).toBe('Worker 1 Alternate Fix');
  });

  it('undoes recent modifications cleanly', () => {
    gramfs.writeFile('hello.txt', 'Version 2', 'main');
    gramfs.writeFile('hello.txt', 'Version 3', 'main');
    expect(gramfs.readFile('hello.txt', 'main')).toBe('Version 3');

    gramfs.undo(1, 'main');
    expect(gramfs.readFile('hello.txt', 'main')).toBe('Version 2');

    gramfs.undo(1, 'main');
    expect(gramfs.readFile('hello.txt', 'main')).toBe('Hello Host World');
  });

  it('atomically commits overlay to host with backup snapshot', () => {
    gramfs.writeFile('hello.txt', 'Committed to Host', 'main');
    gramfs.writeFile('new_file.ts', 'export const x = 1;', 'main');

    const result = gramfs.commitToHost('main');
    expect(result.committedFiles).toContain('hello.txt');
    expect(result.committedFiles).toContain('new_file.ts');

    // Now host has the new content
    expect(fs.readFileSync(path.join(tempDir, 'hello.txt'), 'utf8')).toBe('Committed to Host');
    expect(fs.readFileSync(path.join(tempDir, 'new_file.ts'), 'utf8')).toBe('export const x = 1;');
  });

  it('rejects path traversal security escapes', () => {
    expect(() => gramfs.readFile('../../../etc/passwd')).toThrow();
  });
});

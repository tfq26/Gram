import { describe, it, expect, beforeEach, afterEach } from 'bun:test';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { GramFS } from '../src/core/gramfs';
import { SemanticGraph } from '../src/core/semanticGraph';
import { GramCodeMode } from '../src/core/codeMode';

describe('Gram Code Mode (Single-Turn Programmatic Execution)', () => {
  let tempDir: string;
  let gramfs: GramFS;
  let graph: SemanticGraph;
  let codeMode: GramCodeMode;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'code-mode-test-'));
    fs.writeFileSync(
      path.join(tempDir, 'math.ts'),
      `export function add(a: number, b: number): number {\n  return a + b;\n}`
    );
    gramfs = new GramFS({ workspaceRoot: tempDir });
    graph = new SemanticGraph(gramfs);
    graph.indexFile('math.ts', fs.readFileSync(path.join(tempDir, 'math.ts'), 'utf8'));
    codeMode = new GramCodeMode(gramfs, graph);
  });

  afterEach(() => {
    gramfs.close();
    fs.rmSync(tempDir, { recursive: true, force: true });
  });

  it('runs multi-step operations programmatically in 1 turn', async () => {
    const script = `
      const file = await gram.fs.read("math.ts");
      console.log("Read math.ts with length: " + file.length);
      
      const res = await gram.probe.evalFn("(a, b) => a + b", [10, 20]);
      console.log("Probe result: " + res);
      
      gram.memory.pinFact("math_verified", "add(10, 20) yields 30");
      
      return { probeResult: res, verified: true };
    `;

    const result = await codeMode.execute(script, 'main');

    expect(result.success).toBe(true);
    expect((result.returnValue as any).probeResult).toBe(30);
    expect(result.logs.some((l) => l.includes('Probe result: 30'))).toBe(true);
    expect(codeMode.buildSdk().memory.getFact('math_verified')).toBe('add(10, 20) yields 30');
  });

  it('surgically replaces symbols and verifies in-memory in 1 turn', async () => {
    const script = `
      const uri = "gram://math.ts#function:add";
      const blast = await gram.symbol.blastRadius(uri);
      
      const newCode = "export function add(a: number, b: number): number {\\n  // checked add\\n  return (a + b) | 0;\\n}";
      await gram.symbol.replace("math.ts", uri, newCode);
      
      const updated = await gram.fs.read("math.ts");
      return { updated, blastSeverity: blast.severity };
    `;

    const result = await codeMode.execute(script, 'main');

    expect(result.success).toBe(true);
    expect((result.returnValue as any).updated).toContain('checked add');
    // Ensure host is untouched (still safe in GramFS overlay)
    expect(fs.readFileSync(path.join(tempDir, 'math.ts'), 'utf8')).not.toContain('checked add');
  });

  it('safely handles syntax errors in scripts without crashing the engine', async () => {
    const script = `
      throw new Error("Intentional test assertion failure");
    `;

    const result = await codeMode.execute(script, 'main');

    expect(result.success).toBe(false);
    expect(result.error).toContain('Intentional test assertion failure');
  });
});

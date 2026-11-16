import { afterEach, beforeEach, describe, expect, it, spyOn } from 'bun:test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { GramFS } from '../src/core/gramfs';
import { SemanticGraph } from '../src/core/semanticGraph';
import { NovelTools } from '../src/tools/novelTools';
import { detectWorkspaceToolchain, executeAgentTool } from '../src/runtime/agentTools';

describe('Gram agent coding and Git tools', () => {
  let root: string;
  let gramfs: GramFS;

  beforeEach(() => {
    root = fs.mkdtempSync(path.join(os.tmpdir(), 'gram-agent-tools-'));
    gramfs = new GramFS({ workspaceRoot: root });
  });

  afterEach(() => {
    gramfs.close();
    fs.rmSync(root, { recursive: true, force: true });
  });

  const run = (name: string, input: Record<string, unknown>) => executeAgentTool({
    name, input, workspaceRoot: root, gramfs, layerId: 'main',
  });

  it('stages an exact surgical edit without modifying the host file', async () => {
    fs.writeFileSync(path.join(root, 'example.ts'), 'const count = 1;\n');
    const result = await run('edit_file', { path: 'example.ts', oldText: 'count = 1', newText: 'count = 2' });
    expect(result.isError).toBeUndefined();
    expect(gramfs.readFile('example.ts')).toBe('const count = 2;\n');
    expect(fs.readFileSync(path.join(root, 'example.ts'), 'utf8')).toBe('const count = 1;\n');
  });

  it('applies multi-file changes atomically and rejects stale matches without partial staging', async () => {
    fs.writeFileSync(path.join(root, 'one.ts'), 'one');
    fs.writeFileSync(path.join(root, 'two.ts'), 'two');
    const rejected = await run('apply_changeset', { files: [
      { path: 'one.ts', oldText: 'one', newText: 'ONE' },
      { path: 'two.ts', oldText: 'stale', newText: 'TWO' },
    ] });
    expect(rejected.isError).toBe(true);
    expect(gramfs.listModifiedFiles('main')).toHaveLength(0);

    const accepted = await run('apply_changeset', { files: [
      { path: 'one.ts', oldText: 'one', newText: 'ONE' },
      { path: 'two.ts', oldText: 'two', newText: 'TWO' },
    ] });
    expect(accepted.isError).toBeUndefined();
    expect(gramfs.readFile('one.ts')).toBe('ONE');
    expect(gramfs.readFile('two.ts')).toBe('TWO');
  });

  it('exposes staged diffs and rolls back a requested edit checkpoint', async () => {
    gramfs.writeFile('created.ts', 'export const value = 1;');
    const diff = await run('gramfs_diff', {});
    expect(diff.output).toContain('+export const value = 1;');
    const rollback = await run('rollback_checkpoint', { steps: 1 });
    expect(JSON.parse(rollback.output).undone).toBe(1);
    expect(gramfs.fileExists('created.ts')).toBe(false);
  });

  it('persists a workspace rule through the supplied storage callback', async () => {
    const remembered = await executeAgentTool({
      name: 'remember_rule', input: { rule: 'Prefer small modules.' }, workspaceRoot: root, gramfs, layerId: 'main',
      rememberRule: (rule) => ({ ruleId: 'rule-test', rule, created: true }),
    });
    expect(remembered.output).toContain('Prefer small modules.');
  });

  it('requires a user approval action before committing', async () => {
    const init = Bun.spawnSync(['git', 'init', '-q'], { cwd: root });
    expect(init.exitCode).toBe(0);
    const result = await run('git_commit', { message: 'Add feature' });
    expect(result.requiresApproval).toBe(true);
    expect(result.approvalAction).toEqual({ type: 'git_commit', message: 'Add feature' });
  });

  it('returns structured project validation and test runner outcomes', async () => {
    fs.writeFileSync(path.join(root, 'package.json'), JSON.stringify({ scripts: { typecheck: 'bun --version', lint: 'bun --version', test: 'bun --version' } }));
    const validation = JSON.parse((await run('validate_project', {})).output);
    expect(validation.status).toBe('passed');
    expect(validation.checks.map((check: any) => check.name)).toEqual(['typecheck', 'lint']);
    const tests = JSON.parse((await run('run_tests', {})).output);
    expect(tests.status).toBe('passed');
  });

  it('blocks page fetches to loopback addresses', async () => {
    const result = await run('fetch_web_content', { url: 'http://127.0.0.1:7337/api/health' });
    expect(result.isError).toBe(true);
    expect(result.output.toLowerCase()).toContain('private and local network urls are blocked');
  });

  it('searches with a configured provider, ranks relevant results, and retains citations', async () => {
    const oldKey = process.env.TAVILY_API_KEY;
    process.env.TAVILY_API_KEY = 'test-tavily-key';
    const fetchSpy = spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({ results: [
      { title: 'A related overview', url: 'https://example.com/overview', content: 'Other details.' },
      { title: 'Official Gram search guidance', url: 'https://docs.example.gov/gram-search', content: 'Search web documentation.' },
    ] }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    try {
      const result = await run('search_web', { query: 'Gram search web documentation' });
      expect(result.output.indexOf('Official Gram search guidance')).toBeLessThan(result.output.indexOf('A related overview'));
      expect(result.output).toContain('https://docs.example.gov/gram-search');
    } finally {
      fetchSpy.mockRestore();
      if (oldKey === undefined) delete process.env.TAVILY_API_KEY; else process.env.TAVILY_API_KEY = oldKey;
    }
  });

  it('exposes symbol lookup and a bounded runtime probe', async () => {
    const graph = new SemanticGraph(gramfs);
    const symbolSource = 'export function add(a: number, b: number) { return a + b; }';
    graph.indexFile('math.ts', symbolSource);
    const novelTools = new NovelTools(gramfs, graph);
    const lookup = await executeAgentTool({
      name: 'find_symbol_definition', input: { query: 'add' }, workspaceRoot: root, gramfs, layerId: 'main', graph,
    });
    expect(lookup.output).toContain('gram://math.ts#function:add');
    const probe = await executeAgentTool({
      name: 'runtime_probe', input: { functionSource: '(a, b) => a + b', inputArgs: [2, 3] },
      workspaceRoot: root, gramfs, layerId: 'main', novelTools,
    });
    expect(JSON.parse(probe.output).returnValue).toBe(5);
  });

  it('stages file deletions in GramFS with rollback and commit support', async () => {
    fs.writeFileSync(path.join(root, 'obsolete.ts'), 'export const old = 1;\n');
    const del = await executeAgentTool({
      name: 'delete_file', input: { path: 'obsolete.ts' }, workspaceRoot: root, gramfs, layerId: 'main',
    });
    expect(del.isError).toBeUndefined();
    expect(gramfs.fileExists('obsolete.ts', 'main')).toBe(false);
    expect(fs.existsSync(path.join(root, 'obsolete.ts'))).toBe(true);

    await executeAgentTool({ name: 'rollback_checkpoint', input: { steps: 1 }, workspaceRoot: root, gramfs, layerId: 'main' });
    expect(gramfs.fileExists('obsolete.ts', 'main')).toBe(true);

    await executeAgentTool({ name: 'delete_file', input: { path: 'obsolete.ts' }, workspaceRoot: root, gramfs, layerId: 'main' });
    await executeAgentTool({ name: 'commit_changes', input: {}, workspaceRoot: root, gramfs, layerId: 'main' });
    expect(fs.existsSync(path.join(root, 'obsolete.ts'))).toBe(false);
  });

  it('detects Rust, Go, and Python toolchains and gates dangerous shell commands', async () => {
    fs.writeFileSync(path.join(root, 'Cargo.toml'), '[package]\nname = "demo"\nversion = "0.1.0"\n');
    const rust = detectWorkspaceToolchain(root);
    expect(rust.ecosystems).toContain('Rust (Cargo)');
    expect(rust.testCommand).toEqual(['cargo', 'test']);

    fs.unlinkSync(path.join(root, 'Cargo.toml'));
    fs.writeFileSync(path.join(root, 'go.mod'), 'module example.com/demo\n');
    const go = detectWorkspaceToolchain(root);
    expect(go.ecosystems).toContain('Go');
    expect(go.testCommand).toEqual(['go', 'test', './...']);

    fs.unlinkSync(path.join(root, 'go.mod'));
    fs.writeFileSync(path.join(root, 'pyproject.toml'), '[project]\nname = "demo"\n');
    const py = detectWorkspaceToolchain(root);
    expect(py.ecosystems).toContain('Python');
    expect(py.testCommand).toEqual(['python3', '-m', 'pytest']);

    const gated = await executeAgentTool({
      name: 'run_command', input: { command: 'rm -rf ./dist' }, workspaceRoot: root, gramfs, layerId: 'main',
    });
    expect(gated.requiresApproval).toBe(true);
    expect(gated.approvalAction?.type).toBe('run_command');
  });
});

import { describe, it, expect, beforeEach, afterEach } from 'bun:test';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { GramFS } from '../src/core/gramfs';
import { SemanticGraph } from '../src/core/semanticGraph';
import { ContextEnvelope } from '../src/core/contextEnvelope';
import { NovelTools } from '../src/tools/novelTools';

describe('8 Novel Agent-First Tools', () => {
  let tempDir: string;
  let gramfs: GramFS;
  let graph: SemanticGraph;
  let envelope: ContextEnvelope;
  let tools: NovelTools;

  beforeEach(() => {
    tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'novel-tools-test-'));
    fs.writeFileSync(path.join(tempDir, 'service.ts'), `
      export function parseInput(x: string): number {
        return parseInt(x, 10);
      }
    `);
    gramfs = new GramFS({ workspaceRoot: tempDir });
    graph = new SemanticGraph(gramfs);
    graph.indexFile('service.ts', fs.readFileSync(path.join(tempDir, 'service.ts'), 'utf8'));
    envelope = new ContextEnvelope({ sessionId: 'test-sess', workspacePath: tempDir });
    tools = new NovelTools(gramfs, graph, envelope);
  });

  afterEach(() => {
    gramfs.close();
    fs.rmSync(tempDir, { recursive: true, force: true });
  });

  it('Tool 1 & 4: blastRadiusSimulate and semanticBlameWhy', () => {
    const uri = 'gram://service.ts#function:parseInput';
    const blast = tools.blastRadiusSimulate(uri);
    expect(blast.targetUri).toBe(uri);

    const blame = tools.semanticBlameWhy(uri);
    expect(blame.symbolUri).toBe(uri);
    expect(blame.intentSummary).toContain('parseInput');
  });

  it('Tool 2: runtimeProbe executes micro-evaluations safely', async () => {
    const probe = await tools.runtimeProbe('(a, b) => a * b + 2', [5, 4]);
    expect(probe.returnValue).toBe(22);
    expect(probe.executionTimeMs).toBeGreaterThanOrEqual(0);
  });

  it('Tool 3: hypothesisRace parallelly forks CoW branches and picks the winning fix', async () => {
    const result = await tools.hypothesisRace([
      {
        name: 'failing-hypothesis',
        applyPatch: (fs, layer) => {
          fs.writeFile('service.ts', 'broken code', layer);
        },
        verifyCheck: (fs, layer) => {
          return fs.readFile('service.ts', layer).includes('correct code');
        },
      },
      {
        name: 'winning-hypothesis',
        applyPatch: (fs, layer) => {
          fs.writeFile('service.ts', 'correct code', layer);
        },
        verifyCheck: (fs, layer) => {
          return fs.readFile('service.ts', layer).includes('correct code');
        },
      },
    ]);

    expect(result.winner).toBe('winning-hypothesis');
    expect(gramfs.readFile('service.ts', 'main')).toBe('correct code');
  });

  it('Tool 6: uiGeometryAssert flags bounding box and zIndex occlusions', () => {
    const res = tools.uiGeometryAssert([
      { selector: '#button-save', x: 10, y: 10, width: 100, height: 40, zIndex: 1 },
      { selector: '#overlay-modal', x: 20, y: 20, width: 200, height: 100, zIndex: 1 }, // overlaps at same zIndex
    ]);

    expect(res.valid).toBe(false);
    expect(res.violations.some((v) => v.includes('Layout Occlusion'))).toBe(true);
  });

  it('Tool 7: contextFold compresses turns into pinned facts', () => {
    const fold = tools.contextFold('auth_strategy', 'JWT with 15min rotation');
    expect(fold.key).toBe('auth_strategy');
    expect(envelope.state.pinnedFacts['auth_strategy']).toBe('JWT with 15min rotation');
    expect(envelope.state.headroomCompressedCount).toBe(1);
  });

  it('Tool 8: envSandboxDoctor inspects ports and local environment', async () => {
    const doctor = await tools.envSandboxDoctor([39481]); // test an ephemeral port that should be free
    expect(doctor.openPorts).toContain(39481);
    expect(doctor.osPlatform).toBe(os.platform());
  });

  it('uiVibeCheckAssert detects vibe-coded UI anti-patterns and ContextEnvelope enforces UI_CRAFT_CONSTITUTION', () => {
    const prompt = envelope.compileSystemPrompt();
    expect(prompt).toContain('<UI_CRAFT_CONSTITUTION>');
    expect(prompt).toContain('ZERO EMOJIS IN UI');

    const badSnippet = `
      <div class="bg-gradient-to-r from-indigo-500 to-purple-600 hover:scale-105">
        <h1 style="background-clip: text">🚀 Supercharge Your Workflow</h1>
        <button onclick="alert('Done!')">Click</button>
      </div>
    `;
    const check = tools.uiVibeCheckAssert(badSnippet);
    expect(check.valid).toBe(false);
    expect(check.violations.length).toBeGreaterThanOrEqual(4);

    const cleanSnippet = `
      <div class="panel">
        <h1>Workspace Overview</h1>
        <button type="button"><svg class="svg-icon" viewBox="0 0 16 16"></svg> Run Audit</button>
      </div>
    `;
    expect(tools.uiVibeCheckAssert(cleanSnippet).valid).toBe(true);
  });
});

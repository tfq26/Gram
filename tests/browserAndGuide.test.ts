import { describe, expect, it } from 'bun:test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { GramFS } from '../src/core/gramfs';
import { loadWorkspaceToolGuides } from '../src/runtime/agentRuntime';
import { AGENT_TOOLS, executeAgentTool } from '../src/runtime/agentTools';
import { validateBrowserUrl } from '../src/runtime/browserTools';

describe('Tool guide loading & interactive browser tools', () => {
  it('loads workspace TOOLS.md and AGENTS.md into the system prompt guide', () => {
    const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'gram-guide-'));
    try {
      fs.writeFileSync(path.join(tmpDir, 'TOOLS.md'), '# Custom Tools Guide\nAlways verify with tests.');
      fs.writeFileSync(path.join(tmpDir, 'AGENTS.md'), '# Agent Rules\nKeep edits minimal.');
      const loaded = loadWorkspaceToolGuides(tmpDir);
      expect(loaded).toContain('Workspace Guide (TOOLS.md)');
      expect(loaded).toContain('Always verify with tests.');
      expect(loaded).toContain('Workspace Guide (AGENTS.md)');
      expect(loaded).toContain('Keep edits minimal.');
    } finally {
      fs.rmSync(tmpDir, { recursive: true, force: true });
    }
  });

  it('falls back to Gram built-in TOOLS.md when workspace has no TOOLS.md', () => {
    const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'gram-guide-fallback-'));
    try {
      const loaded = loadWorkspaceToolGuides(tmpDir);
      expect(loaded).toContain('Gram Tool Guide (TOOLS.md)');
      expect(loaded).toContain('search_web');
      expect(loaded).toContain('browser_navigate');
    } finally {
      fs.rmSync(tmpDir, { recursive: true, force: true });
    }
  });

  it('exposes all interactive browser tools in AGENT_TOOLS and validates URLs safely', () => {
    const names = new Set(AGENT_TOOLS.map((t) => t.name));
    for (const toolName of [
      'browser_navigate',
      'browser_snapshot',
      'browser_click',
      'browser_type',
      'browser_screenshot',
      'browser_close',
    ]) {
      expect(names.has(toolName)).toBe(true);
    }

    expect(validateBrowserUrl('http://localhost:3000/app').hostname).toBe('localhost');
    expect(validateBrowserUrl('https://example.com').protocol).toBe('https:');
    expect(() => validateBrowserUrl('file:///etc/passwd')).toThrow();
    expect(() => validateBrowserUrl('javascript:alert(1)')).toThrow();
  });

  it('navigates, types, clicks, snapshots, screenshots, and closes a local web page', async () => {
    const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'gram-browser-'));
    const gramfs = new GramFS({ workspaceRoot: tmpDir });
    const server = Bun.serve({
      port: 0,
      fetch() {
        return new Response(
          `<!doctype html><html><head><title>Test App</title></head><body>
            <input id="name-input" type="text" placeholder="Your name" />
            <button id="greet-btn" onclick="document.getElementById('out').textContent = 'Hello ' + document.getElementById('name-input').value">Greet</button>
            <div id="out">Waiting</div>
          </body></html>`,
          { headers: { 'Content-Type': 'text/html' } }
        );
      },
    });

    const sessionId = `browser-test-${Date.now()}`;
    try {
      const url = `http://127.0.0.1:${server.port}/`;
      const nav = await executeAgentTool({
        name: 'browser_navigate',
        input: { url },
        workspaceRoot: tmpDir,
        gramfs,
        layerId: 'main',
        sessionId,
      });
      expect(nav.isError).toBeFalsy();
      expect(nav.output).toContain('Title: Test App');

      const typed = await executeAgentTool({
        name: 'browser_type',
        input: { selector: '#name-input', text: 'Gram' },
        workspaceRoot: tmpDir,
        gramfs,
        layerId: 'main',
        sessionId,
      });
      expect(typed.isError).toBeFalsy();

      const clicked = await executeAgentTool({
        name: 'browser_click',
        input: { selector: '#greet-btn' },
        workspaceRoot: tmpDir,
        gramfs,
        layerId: 'main',
        sessionId,
      });
      expect(clicked.isError).toBeFalsy();

      const snap = await executeAgentTool({
        name: 'browser_snapshot',
        input: {},
        workspaceRoot: tmpDir,
        gramfs,
        layerId: 'main',
        sessionId,
      });
      expect(snap.isError).toBeFalsy();
      expect(snap.output).toContain('Hello Gram');
      expect(snap.output).toContain('button#greet-btn');

      const shot = await executeAgentTool({
        name: 'browser_screenshot',
        input: { filename: 'test-shot.png' },
        workspaceRoot: tmpDir,
        gramfs,
        layerId: 'main',
        sessionId,
      });
      expect(shot.isError).toBeFalsy();
      expect(fs.existsSync(path.join(tmpDir, '.gram', 'screenshots', 'test-shot.png'))).toBe(true);
    } finally {
      await executeAgentTool({
        name: 'browser_close',
        input: {},
        workspaceRoot: tmpDir,
        gramfs,
        layerId: 'main',
        sessionId,
      });
      server.stop(true);
      fs.rmSync(tmpDir, { recursive: true, force: true });
    }
  }, 25_000);
});

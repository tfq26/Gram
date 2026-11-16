import { afterEach, beforeEach, describe, expect, it } from 'bun:test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { GramFS } from '../src/core/gramfs';
import { initializeProjectGramDir, readProjectContextManifest } from '../src/core/projectInit';
import { loadWorkspaceToolGuides } from '../src/runtime/agentRuntime';
import { executeAgentTool } from '../src/runtime/agentTools';

describe('Project .gram initialization & discovery', () => {
  let root: string;

  beforeEach(() => {
    root = fs.mkdtempSync(path.join(os.tmpdir(), 'gram-project-init-'));
  });

  afterEach(() => {
    fs.rmSync(root, { recursive: true, force: true });
  });

  it('creates .gram/gram.md, .gram/context.json, and .git/info/exclude for a .NET repo without overwriting custom gram.md', () => {
    fs.mkdirSync(path.join(root, '.git'));
    fs.mkdirSync(path.join(root, 'src', 'Aegis.Api'), { recursive: true });
    fs.writeFileSync(path.join(root, 'Aegis.sln'), 'Microsoft Visual Studio Solution File\n');
    fs.writeFileSync(path.join(root, 'src', 'Aegis.Api', 'Program.cs'), 'Console.WriteLine("Aegis");\n');
    fs.writeFileSync(path.join(root, 'README.md'), '# Aegis\nSecurity platform service.\n');

    const res = initializeProjectGramDir(root, { label: 'Aegis' });
    expect(res.createdGramMd).toBe(true);
    expect(res.createdContextJson).toBe(true);
    expect(res.excludedDbFiles).toBe(true);
    expect(res.manifest.ecosystems).toContain('.NET (C#)');
    expect(res.manifest.entrypoints).toContain('Aegis.sln');
    expect(res.manifest.entrypoints).toContain(path.join('src', 'Aegis.Api', 'Program.cs'));
    expect(res.manifest.commands.launch?.command).toContain('dotnet run');

    const gramMd = fs.readFileSync(res.gramMdPath, 'utf8');
    expect(gramMd).toContain('# Aegis — Gram Project Guide');
    expect(gramMd).toContain('Security platform service.');

    const exclude = fs.readFileSync(path.join(root, '.git', 'info', 'exclude'), 'utf8');
    expect(exclude).toContain('.gram/checkpoints.db*');
    expect(exclude).toContain('.gram/gramfs.db*');

    const loaded = readProjectContextManifest(root);
    expect(loaded?.projectName).toBe('Aegis');

    // Custom edits to gram.md are preserved on re-init
    fs.writeFileSync(res.gramMdPath, '# Custom Aegis Guide\nKeep this.\n');
    const second = initializeProjectGramDir(root, { label: 'Aegis' });
    expect(second.createdGramMd).toBe(false);
    expect(fs.readFileSync(res.gramMdPath, 'utf8')).toContain('Keep this.');
  });

  it('loads .gram/gram.md and .gram/context.json in loadWorkspaceToolGuides and supports glob list_files + read_file hints', async () => {
    fs.mkdirSync(path.join(root, 'src', 'Nested'), { recursive: true });
    fs.writeFileSync(path.join(root, 'src', 'Nested', 'Program.cs'), 'class Program {}\n');
    initializeProjectGramDir(root, { label: 'SampleApp' });

    const guide = loadWorkspaceToolGuides(root);
    expect(guide).toContain('Workspace Context (.gram/context.json)');
    expect(guide).toContain('Workspace Guide (.gram/gram.md)');
    expect(guide).toContain('SampleApp');

    const gramfs = new GramFS({ workspaceRoot: root });
    const found = await executeAgentTool({
      name: 'list_files',
      input: { pattern: '**/Program.cs' },
      workspaceRoot: root,
      gramfs,
      layerId: 'main',
    });
    expect(found.output).toContain('src/Nested/Program.cs');

    const missing = await executeAgentTool({
      name: 'read_file',
      input: { path: 'package.json' },
      workspaceRoot: root,
      gramfs,
      layerId: 'main',
    });
    expect(missing.isError).toBe(true);
    expect(missing.output).toContain('Call list_files or search_code');

    const guideUpdate = await executeAgentTool({
      name: 'update_gram_guide',
      input: {
        section: 'Gotchas & Learnings',
        content: 'Run `dotnet run --project src/Nested` on port 5000.',
      },
      workspaceRoot: root,
      gramfs,
      layerId: 'main',
    });
    expect(guideUpdate.isError).toBeUndefined();
    expect(fs.readFileSync(path.join(root, '.gram', 'gram.md'), 'utf8')).toContain(
      'Run `dotnet run --project src/Nested` on port 5000.'
    );
  });
});

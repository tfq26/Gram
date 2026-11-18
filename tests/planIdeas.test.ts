import { describe, expect, test } from 'bun:test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { buildBaselinePlanIdeas, collectWorkspacePlanSignals } from '../src/server/planIdeas';

describe('Context-aware Plan Mode ideas', () => {
  test('detects .NET stack and recent error context for baseline ideas', () => {
    const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'gram-plan-ideas-'));
    try {
      fs.writeFileSync(path.join(tmpDir, 'Aegis.sln'), '', 'utf8');
      const signals = collectWorkspacePlanSignals(tmpDir);
      expect(signals.stack).toBe('dotnet');

      const ideas = buildBaselinePlanIdeas(signals, [
        { role: 'assistant', content: 'Build failed with CS0246', isError: true },
      ]);
      expect(ideas.length).toBe(3);
      expect(ideas[0]).toContain('recent error');
    } finally {
      fs.rmSync(tmpDir, { recursive: true, force: true });
    }
  });
});

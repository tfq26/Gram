/**
 * Loads workspace-specific guide files (.gram/context.json, GRAM.md, CLAUDE.md, TOOLS.md, etc.)
 * into the agent's prompt context.
 */

import fs from 'node:fs';
import path from 'node:path';
import { readProjectContextManifest } from '../core/projectInit';
import { redactSecretText } from '../providers/secretGuard';

const GRAM_BUILTIN_TOOLS_MD = path.resolve(import.meta.dir, '../../TOOLS.md');

export function loadWorkspaceToolGuides(workspaceRoot: string): string {
  const candidates = [
    { label: '.gram/gram.md', filePath: path.join(workspaceRoot, '.gram', 'gram.md') },
    { label: 'GRAM.md', filePath: path.join(workspaceRoot, 'GRAM.md') },
    { label: 'CLAUDE.md', filePath: path.join(workspaceRoot, 'CLAUDE.md') },
    { label: 'TOOLS.md', filePath: path.join(workspaceRoot, 'TOOLS.md') },
    { label: 'AGENTS.md', filePath: path.join(workspaceRoot, 'AGENTS.md') },
    { label: '.gram/TOOLS.md', filePath: path.join(workspaceRoot, '.gram', 'TOOLS.md') },
    { label: '.gram/AGENTS.md', filePath: path.join(workspaceRoot, '.gram', 'AGENTS.md') },
    { label: '.gram/rules.md', filePath: path.join(workspaceRoot, '.gram', 'rules.md') },
  ];
  const sections: string[] = [];
  let totalChars = 0;
  let loadedToolsMd = false;

  const contextManifest = readProjectContextManifest(workspaceRoot);
  if (contextManifest) {
    const cmdEntries = Object.entries(contextManifest.commands || {})
      .filter(([, spec]) => Boolean(spec?.command))
      .map(([kind, spec]) => `- ${kind}: \`${spec!.command}\`${spec!.verified ? ' (verified)' : ''}`);
    const summaryLines = [
      `Project: ${contextManifest.projectName}`,
      contextManifest.ecosystems.length ? `Ecosystems: ${contextManifest.ecosystems.join(', ')}` : '',
      contextManifest.entrypoints.length ? `Entrypoints: ${contextManifest.entrypoints.slice(0, 12).join(', ')}` : '',
      cmdEntries.length ? `Commands:\n${cmdEntries.join('\n')}` : '',
    ].filter(Boolean);
    if (summaryLines.length > 0) {
      const block = `## Workspace Context (.gram/context.json)\n${summaryLines.join('\n')}`;
      sections.push(block);
      totalChars += block.length;
    }
  }

  for (const { label, filePath } of candidates) {
    if (totalChars >= 16_000) break;
    try {
      if (!fs.existsSync(filePath) || !fs.statSync(filePath).isFile()) continue;
      const content = fs.readFileSync(filePath, 'utf8').trim().slice(0, 8_000);
      if (!content) continue;
      sections.push(`## Workspace Guide (${label})\n${content}`);
      totalChars += content.length;
      if (label === 'TOOLS.md' || label === '.gram/TOOLS.md') loadedToolsMd = true;
    } catch {}
  }

  if (!loadedToolsMd && totalChars < 16_000) {
    try {
      if (fs.existsSync(GRAM_BUILTIN_TOOLS_MD) && fs.statSync(GRAM_BUILTIN_TOOLS_MD).isFile()) {
        const content = fs.readFileSync(GRAM_BUILTIN_TOOLS_MD, 'utf8').trim().slice(0, 8_000);
        if (content) sections.push(`## Gram Tool Guide (TOOLS.md)\n${content}`);
      }
    } catch {}
  }

  return redactSecretText(sections.join('\n\n'));
}

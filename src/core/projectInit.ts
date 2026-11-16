import fs from 'node:fs';
import path from 'node:path';
import { detectWorkspaceToolchain } from '../runtime/agentTools';

export interface ProjectCommandSpec {
  command: string;
  verified: boolean;
  lastPassedAt?: string;
  port?: number;
}

export interface ProjectContextManifest {
  schemaVersion: 1;
  projectName: string;
  workspaceRoot: string;
  detectedAt: string;
  ecosystems: string[];
  entrypoints: string[];
  commands: {
    build?: ProjectCommandSpec;
    test?: ProjectCommandSpec;
    validate?: ProjectCommandSpec;
    launch?: ProjectCommandSpec;
  };
  policy: {
    autoApprovePrefixes: string[];
    protectedPaths: string[];
  };
}

export interface ProjectInitResult {
  gramDir: string;
  gramMdPath: string;
  contextJsonPath: string;
  createdGramMd: boolean;
  createdContextJson: boolean;
  excludedDbFiles: boolean;
  manifest: ProjectContextManifest;
}

const SKIP_SCAN_DIRS = new Set([
  '.git',
  '.gram',
  'node_modules',
  'dist',
  'target',
  'bin',
  'obj',
  '.next',
  '__pycache__',
  '.venv',
  'venv',
]);

const ENTRYPOINT_NAMES = new Set([
  'package.json',
  'tsconfig.json',
  'Cargo.toml',
  'go.mod',
  'pyproject.toml',
  'requirements.txt',
  'Dockerfile',
  'docker-compose.yml',
  'docker-compose.yaml',
  'Makefile',
  'launch.sh',
  'README.md',
  'Program.cs',
  'Startup.cs',
  'main.ts',
  'index.ts',
  'server.ts',
  'main.py',
  'app.py',
  'main.rs',
  'lib.rs',
  'main.go',
]);

const GIT_EXCLUDE_ENTRIES = ['.gram/checkpoints.db*', '.gram/gramfs.db*'];

function readJsonSafe(filePath: string): Record<string, any> | null {
  try {
    return JSON.parse(fs.readFileSync(filePath, 'utf8'));
  } catch {
    return null;
  }
}

function readReadmeSummary(workspaceRoot: string): string {
  for (const name of ['README.md', 'readme.md', 'README.txt']) {
    const candidate = path.join(workspaceRoot, name);
    if (!fs.existsSync(candidate)) continue;
    try {
      const lines = fs
        .readFileSync(candidate, 'utf8')
        .split('\n')
        .map((l) => l.trim())
        .filter((l) => l && !l.startsWith('![') && !l.startsWith('[!['));
      const excerpt = lines.slice(0, 6).join(' ').replace(/\s+/g, ' ').trim();
      if (excerpt) return excerpt.slice(0, 320);
    } catch {}
  }
  return '';
}

function discoverEntrypoints(workspaceRoot: string): { topLevel: string[]; entrypoints: string[] } {
  const topLevel: string[] = [];
  const entrypoints: string[] = [];

  try {
    const rootEntries = fs.readdirSync(workspaceRoot, { withFileTypes: true });
    for (const entry of rootEntries) {
      if (entry.name.startsWith('.') && entry.name !== '.env.example') continue;
      if (SKIP_SCAN_DIRS.has(entry.name)) continue;
      topLevel.push(entry.isDirectory() ? `${entry.name}/` : entry.name);
      if (
        !entry.isDirectory() &&
        (ENTRYPOINT_NAMES.has(entry.name) || entry.name.endsWith('.sln') || entry.name.endsWith('.csproj'))
      ) {
        entrypoints.push(entry.name);
      }
    }
  } catch {}

  const walkNested = (dir: string, depth: number) => {
    if (depth > 3 || entrypoints.length >= 24) return;
    let entries: fs.Dirent[] = [];
    try {
      entries = fs.readdirSync(dir, { withFileTypes: true });
    } catch {
      return;
    }
    for (const entry of entries) {
      if (SKIP_SCAN_DIRS.has(entry.name) || entry.name.startsWith('.')) continue;
      const full = path.join(dir, entry.name);
      const rel = path.relative(workspaceRoot, full);
      if (entry.isDirectory()) {
        walkNested(full, depth + 1);
      } else if (ENTRYPOINT_NAMES.has(entry.name) || entry.name.endsWith('.csproj')) {
        if (!entrypoints.includes(rel)) entrypoints.push(rel);
      }
    }
  };

  for (const sub of ['src', 'app', 'cmd', 'crates', 'services']) {
    const subPath = path.join(workspaceRoot, sub);
    if (fs.existsSync(subPath)) walkNested(subPath, 1);
  }

  return {
    topLevel: topLevel.slice(0, 30),
    entrypoints: entrypoints.slice(0, 24),
  };
}

function inferCommands(workspaceRoot: string, entrypoints: string[]): ProjectContextManifest['commands'] {
  const toolchain = detectWorkspaceToolchain(workspaceRoot);
  const pkg = readJsonSafe(path.join(workspaceRoot, 'package.json'));
  const scripts = (pkg?.scripts || {}) as Record<string, string>;
  const commands: ProjectContextManifest['commands'] = {};

  if (scripts.build) {
    commands.build = { command: 'bun run build', verified: false };
  } else if (toolchain.ecosystems.includes('Rust (Cargo)')) {
    commands.build = { command: 'cargo build', verified: false };
  } else if (toolchain.ecosystems.includes('.NET (C#)')) {
    commands.build = { command: 'dotnet build', verified: false };
  } else if (toolchain.ecosystems.includes('Go')) {
    commands.build = { command: 'go build ./...', verified: false };
  }

  if (toolchain.testCommand) {
    commands.test = { command: toolchain.testCommand.join(' '), verified: false };
  }

  if (toolchain.validateChecks.length > 0) {
    commands.validate = {
      command: toolchain.validateChecks.map((c) => c.command.join(' ')).join(' && '),
      verified: false,
    };
  }

  if (scripts.dev) {
    commands.launch = { command: 'bun run dev', verified: false };
  } else if (scripts.start) {
    commands.launch = { command: 'bun run start', verified: false };
  } else if (fs.existsSync(path.join(workspaceRoot, 'launch.sh'))) {
    commands.launch = { command: './launch.sh', verified: false };
  } else if (toolchain.ecosystems.includes('.NET (C#)')) {
    const runnable = entrypoints.find((e) => e.endsWith('Program.cs'));
    const projectDir = runnable ? path.dirname(runnable) : null;
    commands.launch = {
      command: projectDir && projectDir !== '.' ? `dotnet run --project ${projectDir}` : 'dotnet run',
      verified: false,
    };
  } else if (toolchain.ecosystems.includes('Rust (Cargo)')) {
    commands.launch = { command: 'cargo run', verified: false };
  } else if (toolchain.ecosystems.includes('Go')) {
    commands.launch = { command: 'go run .', verified: false };
  }

  return commands;
}

function ensureGitInfoExclude(workspaceRoot: string): boolean {
  const gitDir = path.join(workspaceRoot, '.git');
  if (!fs.existsSync(gitDir)) return false;
  const infoDir = path.join(gitDir, 'info');
  const excludePath = path.join(infoDir, 'exclude');
  try {
    fs.mkdirSync(infoDir, { recursive: true });
    const existing = fs.existsSync(excludePath) ? fs.readFileSync(excludePath, 'utf8') : '';
    const lines = existing.split('\n').map((l) => l.trim());
    const missing = GIT_EXCLUDE_ENTRIES.filter((entry) => !lines.includes(entry));
    if (missing.length === 0) return false;
    const prefix = existing && !existing.endsWith('\n') ? '\n' : '';
    fs.writeFileSync(excludePath, `${existing}${prefix}# Gram local runtime databases\n${missing.join('\n')}\n`, 'utf8');
    return true;
  } catch {
    return false;
  }
}

function renderDefaultGramMd(manifest: ProjectContextManifest, readmeSummary: string, topLevel: string[]): string {
  const stackLine = manifest.ecosystems.length > 0 ? manifest.ecosystems.join(', ') : 'General / Unclassified';
  const entryLines =
    manifest.entrypoints.length > 0
      ? manifest.entrypoints.map((e) => `- \`${e}\``).join('\n')
      : topLevel.map((e) => `- \`${e}\``).join('\n') || '- (Run `list_files` to inspect workspace)';

  const cmdLines = [
    manifest.commands.build ? `- **Build**: \`${manifest.commands.build.command}\`` : '',
    manifest.commands.launch ? `- **Run / Launch**: \`${manifest.commands.launch.command}\`` : '',
    manifest.commands.test ? `- **Test**: \`${manifest.commands.test.command}\`` : '',
    manifest.commands.validate ? `- **Validate**: \`${manifest.commands.validate.command}\`` : '',
  ]
    .filter(Boolean)
    .join('\n');

  return [
    `# ${manifest.projectName} — Gram Project Guide`,
    '',
    '## Overview',
    readmeSummary || `Workspace at \`${manifest.workspaceRoot}\`. Stack: ${stackLine}.`,
    '',
    `## Stack`,
    `- **Ecosystems**: ${stackLine}`,
    topLevel.length > 0 ? `- **Top-level entries**: ${topLevel.map((e) => `\`${e}\``).join(', ')}` : '',
    '',
    '## Key Entrypoints & Manifests',
    entryLines,
    '',
    '## Commands',
    cmdLines || '- Inspect manifests or `README.md` before running build/launch commands.',
    '',
    '## Conventions & Guardrails',
    '- Always inspect existing files with `read_file` or `list_files` before editing.',
    '- Protected paths (require confirmation): `.env*`, `migrations/`.',
    '- Add repo-specific gotchas, required env vars, and verified commands below as you discover them.',
    '',
    '## Gotchas & Learnings',
    '- (None recorded yet)',
    '',
  ]
    .filter((line, idx, arr) => !(line === '' && arr[idx - 1] === ''))
    .join('\n');
}

/**
 * Reads `.gram/context.json` from a workspace if present and valid.
 */
export function readProjectContextManifest(workspaceRoot: string): ProjectContextManifest | null {
  const contextPath = path.join(path.resolve(workspaceRoot), '.gram', 'context.json');
  const parsed = readJsonSafe(contextPath);
  if (!parsed || parsed.schemaVersion !== 1) return null;
  return parsed as ProjectContextManifest;
}

/**
 * Initializes `<workspaceRoot>/.gram/` with `gram.md`, `context.json`, and local `.git/info/exclude` entries.
 * Never overwrites an existing `gram.md` unless `force` is true, and preserves verified commands in `context.json`.
 */
export function initializeProjectGramDir(
  workspaceRoot: string,
  options: { label?: string; force?: boolean } = {}
): ProjectInitResult {
  const resolvedRoot = path.resolve(workspaceRoot);
  const gramDir = path.join(resolvedRoot, '.gram');
  const gramMdPath = path.join(gramDir, 'gram.md');
  const contextJsonPath = path.join(gramDir, 'context.json');

  fs.mkdirSync(gramDir, { recursive: true });

  const toolchain = detectWorkspaceToolchain(resolvedRoot);
  const { topLevel, entrypoints } = discoverEntrypoints(resolvedRoot);
  const inferredCommands = inferCommands(resolvedRoot, entrypoints);
  const existingContext = readProjectContextManifest(resolvedRoot);

  const mergedCommands: ProjectContextManifest['commands'] = {
    build: existingContext?.commands?.build?.verified ? existingContext.commands.build : inferredCommands.build || existingContext?.commands?.build,
    test: existingContext?.commands?.test?.verified ? existingContext.commands.test : inferredCommands.test || existingContext?.commands?.test,
    validate: existingContext?.commands?.validate?.verified ? existingContext.commands.validate : inferredCommands.validate || existingContext?.commands?.validate,
    launch: existingContext?.commands?.launch?.verified ? existingContext.commands.launch : inferredCommands.launch || existingContext?.commands?.launch,
  };

  const manifest: ProjectContextManifest = {
    schemaVersion: 1,
    projectName: options.label?.trim() || existingContext?.projectName || path.basename(resolvedRoot) || 'Project',
    workspaceRoot: resolvedRoot,
    detectedAt: new Date().toISOString(),
    ecosystems: toolchain.ecosystems,
    entrypoints,
    commands: mergedCommands,
    policy: existingContext?.policy || {
      autoApprovePrefixes: ['git status', 'git diff', 'git log', 'ls', 'cat'],
      protectedPaths: ['.env', '.env.*', 'migrations/'],
    },
  };

  let createdContextJson = false;
  if (!fs.existsSync(contextJsonPath) || options.force) {
    fs.writeFileSync(contextJsonPath, JSON.stringify(manifest, null, 2) + '\n', 'utf8');
    createdContextJson = true;
  }

  let createdGramMd = false;
  if (!fs.existsSync(gramMdPath) || options.force) {
    const readmeSummary = readReadmeSummary(resolvedRoot);
    fs.writeFileSync(gramMdPath, renderDefaultGramMd(manifest, readmeSummary, topLevel), 'utf8');
    createdGramMd = true;
  }

  const excludedDbFiles = ensureGitInfoExclude(resolvedRoot);

  return {
    gramDir,
    gramMdPath,
    contextJsonPath,
    createdGramMd,
    createdContextJson,
    excludedDbFiles,
    manifest,
  };
}

export type GramGuideSection =
  | 'Overview'
  | 'Commands'
  | 'Conventions & Guardrails'
  | 'Gotchas & Learnings';

/**
 * Appends or replaces content under a named `## <section>` heading in `<workspaceRoot>/.gram/gram.md`.
 * Initializes `.gram/gram.md` first if it does not exist yet.
 */
export function updateProjectGramMd(
  workspaceRoot: string,
  section: GramGuideSection,
  content: string,
  mode: 'append' | 'replace' = 'append'
): { updatedPath: string; section: GramGuideSection } {
  const clean = content.trim();
  if (!clean) {
    throw new Error('Cannot write empty content to .gram/gram.md.');
  }
  const { gramMdPath } = initializeProjectGramDir(workspaceRoot);
  const existing = fs.readFileSync(gramMdPath, 'utf8');
  const heading = `## ${section}`;
  const lines = existing.split('\n');
  const startIdx = lines.findIndex((l) => l.trim().toLowerCase() === heading.toLowerCase());

  if (startIdx === -1) {
    const sep = existing.endsWith('\n') ? '\n' : '\n\n';
    fs.writeFileSync(gramMdPath, `${existing}${sep}${heading}\n${clean}\n`, 'utf8');
    return { updatedPath: gramMdPath, section };
  }

  let endIdx = lines.length;
  for (let i = startIdx + 1; i < lines.length; i++) {
    if (/^##\s+/.test(lines[i])) {
      endIdx = i;
      break;
    }
  }

  const currentBody = lines
    .slice(startIdx + 1, endIdx)
    .filter((l) => l.trim() !== '- (None recorded yet)')
    .join('\n')
    .trim();

  const nextBody =
    mode === 'replace' || !currentBody
      ? clean
      : currentBody.includes(clean)
        ? currentBody
        : `${currentBody}\n${clean.startsWith('-') ? clean : `- ${clean}`}`;

  const updated = [
    ...lines.slice(0, startIdx + 1),
    nextBody,
    '',
    ...lines.slice(endIdx),
  ]
    .join('\n')
    .replace(/\n{3,}/g, '\n\n');

  fs.writeFileSync(gramMdPath, updated.endsWith('\n') ? updated : `${updated}\n`, 'utf8');
  return { updatedPath: gramMdPath, section };
}

import fs from 'node:fs';
import path from 'node:path';
import { selectOptimalModel } from '../providers/router';
import { streamLlmTurn } from '../providers/llmClient';

export interface PlanIdeaMessage {
  role: string;
  content: string;
  isError?: boolean;
}

export interface WorkspacePlanSignals {
  projectName: string;
  stack: string;
  topEntries: string[];
  gramMdSummary: string;
  gitStatusLines: string[];
  gitCommits: string[];
  gitHead: string;
  hasChanges: boolean;
}

interface CachedPlanIdeas {
  ideas: string[];
  createdAt: number;
}

const PLAN_IDEAS_CACHE = new Map<string, CachedPlanIdeas>();
const CACHE_TTL_MS = 5 * 60 * 1000;
const MAX_CACHE_ENTRIES = 50;

function runGit(workspaceRoot: string, args: string[]): string {
  try {
    const proc = Bun.spawnSync(['git', '-C', workspaceRoot, ...args], {
      stdout: 'pipe',
      stderr: 'pipe',
    });
    if (proc.exitCode !== 0) return '';
    return new TextDecoder().decode(proc.stdout).trim();
  } catch {
    return '';
  }
}

export function collectWorkspacePlanSignals(workspaceRoot: string): WorkspacePlanSignals {
  const projectName = path.basename(workspaceRoot) || 'workspace';
  let topEntries: string[] = [];
  try {
    topEntries = fs
      .readdirSync(workspaceRoot, { withFileTypes: true })
      .filter((e) => !['.git', 'node_modules', 'bin', 'obj', 'target', '.gram', 'dist'].includes(e.name))
      .slice(0, 24)
      .map((e) => (e.isDirectory() ? `${e.name}/` : e.name));
  } catch {}

  const hasExt = (ext: string) => topEntries.some((e) => e.toLowerCase().endsWith(ext));
  let stack = 'general';
  if (hasExt('.sln') || hasExt('.csproj')) stack = 'dotnet';
  else if (topEntries.includes('Cargo.toml')) stack = 'rust';
  else if (topEntries.includes('go.mod')) stack = 'go';
  else if (topEntries.includes('pyproject.toml') || topEntries.includes('requirements.txt')) stack = 'python';
  else if (topEntries.includes('package.json')) stack = 'node';

  let gramMdSummary = '';
  try {
    const gramMdPath = path.join(workspaceRoot, '.gram', 'gram.md');
    if (fs.existsSync(gramMdPath)) {
      gramMdSummary = fs.readFileSync(gramMdPath, 'utf8').slice(0, 1200).trim();
    }
  } catch {}

  const gitStatusRaw = runGit(workspaceRoot, ['status', '--short']);
  const gitStatusLines = gitStatusRaw ? gitStatusRaw.split('\n').slice(0, 12) : [];
  const gitLogRaw = runGit(workspaceRoot, ['log', '-n', '3', '--oneline']);
  const gitCommits = gitLogRaw ? gitLogRaw.split('\n').slice(0, 3) : [];
  const gitHead = runGit(workspaceRoot, ['rev-parse', '--short', 'HEAD']);

  return {
    projectName,
    stack,
    topEntries,
    gramMdSummary,
    gitStatusLines,
    gitCommits,
    gitHead,
    hasChanges: gitStatusLines.length > 0,
  };
}

export function buildBaselinePlanIdeas(
  signals: WorkspacePlanSignals,
  recentMessages: PlanIdeaMessage[] = []
): string[] {
  const ideas: string[] = [];
  const lastMsg = recentMessages[recentMessages.length - 1];
  const hasRecentError = recentMessages.some((m) => m.isError);

  if (hasRecentError) {
    ideas.push('Diagnose and plan fix for recent error');
  } else if (lastMsg?.content?.trim()) {
    ideas.push('Plan next steps from current thread');
  }

  if (signals.hasChanges) {
    ideas.push('Plan tests for uncommitted changes');
  }

  const stackIdeas: Record<string, string[]> = {
    dotnet: [
      `Design new ${signals.projectName} feature spec`,
      'Plan runtime contracts & test coverage',
      'Audit error handling & edge cases',
    ],
    rust: [
      `Design new ${signals.projectName} module`,
      'Plan trait boundaries & safety checks',
      'Map benchmark & test improvements',
    ],
    go: [
      `Design new ${signals.projectName} service flow`,
      'Plan concurrency & error handling refactor',
      'Expand integration test coverage',
    ],
    python: [
      `Design new ${signals.projectName} workflow`,
      'Plan module refactor & type coverage',
      'Add regression tests for core paths',
    ],
    node: [
      `Design new ${signals.projectName} feature spec`,
      'Plan safe module & state refactor',
      'Map end-to-end test coverage',
    ],
    general: [
      `Design new ${signals.projectName} feature spec`,
      'Plan a safe architecture refactor',
      'Trace a bug and map the fix',
    ],
  };

  for (const candidate of stackIdeas[signals.stack] || stackIdeas.general) {
    if (ideas.length >= 3) break;
    if (!ideas.includes(candidate)) ideas.push(candidate);
  }

  return ideas.slice(0, 3);
}

function parseIdeasFromText(raw: string): string[] | null {
  const trimmed = raw.trim();
  const jsonMatch = trimmed.match(/\[[\s\S]*\]/);
  if (!jsonMatch) return null;
  try {
    const parsed = JSON.parse(jsonMatch[0]);
    if (!Array.isArray(parsed)) return null;
    const cleaned = parsed
      .map((item) => String(item || '').replace(/[\r\n]+/g, ' ').trim())
      .filter((s) => s.length >= 6 && s.length <= 64);
    return cleaned.length >= 3 ? cleaned.slice(0, 3) : null;
  } catch {
    return null;
  }
}

export async function generateContextAwarePlanIdeas(params: {
  workspaceRoot: string;
  recentMessages?: PlanIdeaMessage[];
  authToken?: string;
  signal?: AbortSignal;
}): Promise<{ ideas: string[]; source: 'cache' | 'llm' | 'baseline' }> {
  const recentMessages = (params.recentMessages || []).slice(-4);
  const signals = collectWorkspacePlanSignals(params.workspaceRoot);
  const baseline = buildBaselinePlanIdeas(signals, recentMessages);

  const lastMsgSnippet = recentMessages.map((m) => `${m.role}:${m.content.slice(0, 80)}`).join('|');
  const cacheKey = `${params.workspaceRoot}::${signals.gitHead}::${signals.gitStatusLines.join(',')}::${lastMsgSnippet}`;
  const cached = PLAN_IDEAS_CACHE.get(cacheKey);
  if (cached && Date.now() - cached.createdAt < CACHE_TTL_MS) {
    return { ideas: cached.ideas, source: 'cache' };
  }

  const contextParts = [
    `Project: ${signals.projectName} (stack: ${signals.stack})`,
    signals.topEntries.length ? `Top-level entries: ${signals.topEntries.slice(0, 16).join(', ')}` : '',
    signals.gramMdSummary ? `Project guide (.gram/gram.md):\n${signals.gramMdSummary}` : '',
    signals.gitStatusLines.length ? `Uncommitted changes:\n${signals.gitStatusLines.join('\n')}` : '',
    signals.gitCommits.length ? `Recent commits:\n${signals.gitCommits.join('\n')}` : '',
    recentMessages.length
      ? `Recent conversation:\n${recentMessages
          .map((m) => `- ${m.role}${m.isError ? ' (error)' : ''}: ${m.content.slice(0, 220)}`)
          .join('\n')}`
      : '',
  ]
    .filter(Boolean)
    .join('\n\n');

  try {
    const economyModel = selectOptimalModel({ taskComplexity: 'simple', priority: 'cost' });
    const turn = await streamLlmTurn({
      model: economyModel,
      systemPrompt:
        'You generate 3 concise single-line planning ideas tailored to the user’s project and recent conversation. Return ONLY a JSON array of 3 strings. Each string must be 4 to 7 words (max 52 characters), specific to the codebase or conversation, with no trailing punctuation or numbering.',
      messages: [{ role: 'user', content: contextParts }],
      tools: [],
      effort: 'low',
      authToken: params.authToken,
      signal: params.signal ?? AbortSignal.timeout(7000),
    });

    const parsed = parseIdeasFromText(turn.text || '');
    if (parsed) {
      if (PLAN_IDEAS_CACHE.size >= MAX_CACHE_ENTRIES) {
        const oldestKey = PLAN_IDEAS_CACHE.keys().next().value;
        if (oldestKey) PLAN_IDEAS_CACHE.delete(oldestKey);
      }
      PLAN_IDEAS_CACHE.set(cacheKey, { ideas: parsed, createdAt: Date.now() });
      return { ideas: parsed, source: 'llm' };
    }
  } catch {
    // Fall back cleanly to deterministic Layer 1 ideas
  }

  return { ideas: baseline, source: 'baseline' };
}

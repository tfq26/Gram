/**
 * Gram v2 Unified Server & Developer Environment
 *
 * The web shell follows Gram's original first-commit three-pane conversation UI.
 * V2-only surfaces are intentionally deferred in the browser UI and tracked in
 * docs/v2-feature-review.md; the runtime and API remain available to the CLI.
 */

import path from 'node:path';
import fs from 'node:fs';
import os from 'node:os';
import { GramAgentRuntime } from '../runtime/agentRuntime';
import { ContextEnvelope } from '../core/contextEnvelope';
import { formatUnifiedDiff } from '../runtime/agentTools';
import { handleProjectRoutes } from './routes/projects';
import { handleSystemRoutes } from './routes/processes';
import { handleAuthRoutes } from './routes/auth';
import { handleLyraRoutes } from './routes/lyra';
import { handleFileRoutes } from './routes/files';
import { loadCachedModelCatalog, refreshModelCatalogIfStale } from '../providers/modelCatalog';
import { handleModelRoutes } from './routes/models';
import { handleUserContextRoutes } from './routes/userContext';
import { errorResponse, jsonResponse } from './http';
import { AHPServer } from '../sync/ahpProtocol';
import { GramConnectManager } from '../sync/connectPresence';
import { AUTO_MODEL_ID, isAllowedModel, listAllowedModels, selectOptimalModel } from '../providers/router';
import { streamLlmTurn } from '../providers/llmClient';
import { extractCookieValue, fetchLyraAuthProviders, fetchLyraAuthState, proxyLyraJson } from './lyraClient';
import { listAvailableVoices, readAudioBody, synthesizeSpeech, transcribeAudio } from './voice';
import { sharedProcessManager } from '../runtime/processManager';
import { getAppHtml } from './classicShell';
import { generateContextAwarePlanIdeas, type PlanIdeaMessage } from './planIdeas';

const cwd = process.cwd();
const runtime = new GramAgentRuntime(cwd);
const ahp = new AHPServer();
const connect = new GramConnectManager();
const classicAssetsRoot = path.resolve(cwd, 'src/server/classic-assets');

// Model catalog: start from the last good copy on disk, then refresh from Lyra in the background.
if (process.env.NODE_ENV !== 'test') {
  loadCachedModelCatalog();
  void refreshModelCatalogIfStale();
}

const PORT = parseInt(process.env.GRAM_PORT || (process.env.NODE_ENV === 'test' ? '0' : '7337'), 10);

function extractRequestAuthToken(req: Request): string | undefined {
  const cookieToken = extractCookieValue(req.headers.get('cookie'), 'gram_lyra_token');
  if (cookieToken) return cookieToken;
  const authHeader = req.headers.get('authorization');
  if (authHeader && authHeader.toLowerCase().startsWith('bearer ')) {
    const bearer = authHeader.slice(7).trim();
    if (bearer) return bearer;
  }
  return undefined;
}

connect.recordHeartbeat({
  deviceId: 'studio-local',
  deviceName: `Gram Desktop (${path.basename(cwd)})`,
  deviceType: 'desktop',
  workspacePath: cwd,
  isExecutionHost: true,
});

let activeSyncPromise: Promise<{ synced: boolean; latestSeq: number }> | null = null;

async function syncWithLyraHub(req?: Request): Promise<{ synced: boolean; latestSeq: number }> {
  if (activeSyncPromise) return activeSyncPromise;
  activeSyncPromise = (async () => {
    const dirty = runtime.getDirtySyncPayload();
    try {
      const res = await proxyLyraJson(
        req || new Request('http://localhost/api/sync'),
        '/api/ahp/sync',
        { method: 'POST', body: dirty }
      );
      if (!res.ok) {
        return { synced: false, latestSeq: dirty.sinceSeq };
      }
      const delta = (await res.json()) as {
        latestSeq: number;
        chats?: any[];
        tasks?: any[];
        deletedChatIds?: string[];
      };
      runtime.applyRemoteSyncDelta({
        ...delta,
        pushedChats: dirty.chats.map((c) => ({ sessionId: c.sessionId, revision: c.revision })),
        pushedRunKeys: dirty.tasks.map((t) => ({ sessionId: t.sessionId, runId: t.runId })),
      });
      return { synced: true, latestSeq: delta.latestSeq ?? dirty.sinceSeq };
    } catch {
      return { synced: false, latestSeq: dirty.sinceSeq };
    } finally {
      activeSyncPromise = null;
    }
  })();
  return activeSyncPromise;
}

function createLyraToolBridge(req: Request) {
  const json = async (pathname: string, method: string, body?: unknown) => {
    const response = await proxyLyraJson(req, pathname, { method, body });
    const data = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error((data as any)?.error || `Lyra Hub returned HTTP ${response.status}.`);
    return data;
  };
  return {
    escalate: (payload: Record<string, unknown>) => {
      const objective = String(payload.objective || '');
      const pinnedFacts = (payload.pinnedFacts && typeof payload.pinnedFacts === 'object' ? payload.pinnedFacts : {}) as Record<string, string>;
      const overlay = Array.isArray(payload.workspaceDiff) ? payload.workspaceDiff as Array<{ path: string; content: string; isDeleted?: boolean }> : [];
      const safeOverlay = overlay.filter((file) => !/(^|\/)\.env(?:\.|$)|(^|\/)(?:secrets?|credentials?)(?:\/|\.|$)|\.(?:pem|key)$/i.test(file.path));
      let budget = 350_000;
      const context = safeOverlay.slice(0, 100).map((file) => {
        const content = String(file.content || '').slice(0, Math.max(0, budget));
        budget -= content.length;
        return `### ${file.isDeleted ? 'Deleted' : 'Staged'}: ${file.path}\n${file.isDeleted ? '(deleted)' : content}`;
      }).join('\n\n');
      const prompt = [
        objective,
        Object.keys(pinnedFacts).length ? `## Verified local context\n${Object.entries(pinnedFacts).map(([key, value]) => `- ${key}: ${value}`).join('\n')}` : '',
        context ? `## Current GramFS overlay snapshot\n${context}` : '',
      ].filter(Boolean).join('\n\n');
      return json('/api/ahp/escalate', 'POST', { sessionId: payload.sessionId, prompt, workspacePath: cwd, pinnedFacts, workspaceDiff: safeOverlay });
    },
    status: (runId: string, sessionId?: string) => json(`/api/gateway/runs/${encodeURIComponent(runId)}/status?sessionId=${encodeURIComponent(sessionId || '')}`, 'GET'),
  };
}

async function handleRequest(req: Request): Promise<Response> {
    const url = new URL(req.url);

    const userContextResponse = await handleUserContextRoutes(req, url, runtime);
    if (userContextResponse) return userContextResponse;

    if (req.method === 'GET' && (url.pathname === '/manifest.webmanifest' || url.pathname === '/sw.js' || url.pathname.startsWith('/icons/'))) {
      for (const rootDir of [path.resolve(cwd, 'dist/studio'), path.resolve(cwd, 'public')]) {
        const assetPath = path.resolve(rootDir, '.' + decodeURIComponent(url.pathname));
        if (assetPath.startsWith(rootDir + path.sep) && fs.existsSync(assetPath) && fs.statSync(assetPath).isFile()) {
          const extension = path.extname(assetPath).toLowerCase();
          const contentType =
            extension === '.webmanifest'
              ? 'application/manifest+json; charset=utf-8'
              : extension === '.js'
                ? 'text/javascript; charset=utf-8'
                : extension === '.svg'
                  ? 'image/svg+xml'
                  : extension === '.png'
                    ? 'image/png'
                    : 'application/octet-stream';
          const cacheControl = url.pathname === '/sw.js' ? 'no-cache' : 'public, max-age=3600';
          return new Response(fs.readFileSync(assetPath), {
            headers: { 'Content-Type': contentType, 'Cache-Control': cacheControl },
          });
        }
      }
    }

    if (['/app.css', '/shadcn.css', '/v2-adapter.css', '/audio.js'].includes(url.pathname) || url.pathname.startsWith('/icons/') || url.pathname.startsWith('/logo/')) {
      const assetPath = path.resolve(classicAssetsRoot, '.' + decodeURIComponent(url.pathname));
      if (assetPath.startsWith(classicAssetsRoot + path.sep) && fs.existsSync(assetPath) && fs.statSync(assetPath).isFile()) {
        const extension = path.extname(assetPath).toLowerCase();
        const contentType = extension === '.css' ? 'text/css; charset=utf-8' : extension === '.js' ? 'text/javascript; charset=utf-8' : extension === '.svg' ? 'image/svg+xml' : extension === '.webp' ? 'image/webp' : extension === '.png' ? 'image/png' : 'application/octet-stream';
        return new Response(fs.readFileSync(assetPath), { headers: { 'Content-Type': contentType } });
      }
    }

    if (req.method === 'GET' && url.pathname.startsWith('/assets/')) {
      const studioAssetsRoot = path.resolve(cwd, 'dist/studio/assets');
      const assetPath = path.resolve(studioAssetsRoot, url.pathname.slice('/assets/'.length));
      if (assetPath.startsWith(studioAssetsRoot + path.sep) && fs.existsSync(assetPath) && fs.statSync(assetPath).isFile()) {
        const extension = path.extname(assetPath).toLowerCase();
        const contentType =
          extension === '.css'
            ? 'text/css; charset=utf-8'
            : extension === '.js'
              ? 'text/javascript; charset=utf-8'
              : extension === '.woff2'
                ? 'font/woff2'
                : extension === '.woff'
                  ? 'font/woff'
                  : 'application/octet-stream';
        return new Response(fs.readFileSync(assetPath), { headers: { 'Content-Type': contentType } });
      }
    }

    if (
      url.pathname === '/' ||
      url.pathname === '/index.html' ||
      url.pathname === '/settings' ||
      url.pathname === '/login' ||
      url.pathname === '/signup' ||
      url.pathname === '/welcome' ||
      url.pathname === '/onboarding'
    ) {
      const studioIndex = path.resolve(cwd, 'dist/studio/index.html');
      if (fs.existsSync(studioIndex)) {
        return new Response(fs.readFileSync(studioIndex, 'utf8'), {
          headers: { 'Content-Type': 'text/html; charset=utf-8' },
        });
      }
      return new Response(getAppHtml(), {
        headers: { 'Content-Type': 'text/html; charset=utf-8' },
      });
    }

    if (url.pathname === '/api/health') {
      return jsonResponse({ status: 'ok', version: '2.0.0', workspace: cwd });
    }

    if (url.pathname === '/api/workspace') {
      const vcs = runtime.gramfs.detectVcs();
      return jsonResponse({
        name: path.basename(cwd),
        path: cwd,
        vcs,
      });
    }

    if (url.pathname === '/api/tasks') {
      const testSessions = new Set(['sess-1', 'sess-2', 'sess-3', 's-sync', 's-async', 's-resume', 'test-session']);
      const tasks = runtime.listRecentTasks(30).filter((t) => !testSessions.has(t.sessionId));
      return jsonResponse(tasks);
    }

    if (url.pathname === '/api/tasks/continue' && req.method === 'POST') {
      try {
        const body = (await req.json()) as { sessionId?: string; runId?: string; feedback?: string; modelId?: string };
        const sessionId = String(body.sessionId || '').trim();
        const runId = String(body.runId || '').trim();
        const feedback = String(body.feedback || '').trim();
        if (!sessionId || !runId || !feedback) {
          return errorResponse('sessionId, runId, and feedback are required.', 400);
        }
        const ok = await runtime.resumeRun(sessionId, runId, {
          feedback,
          modelId: body.modelId,
          authToken: extractRequestAuthToken(req),
        });
        const checkpoint = runtime.getCheckpoint(sessionId, runId);
        return jsonResponse({ ok, checkpoint }, ok ? 200 : 404);
      } catch (err: any) {
        return errorResponse(err?.message || 'Failed to continue task.', 500);
      }
    }

    if (url.pathname === '/api/plan/ideas' && req.method === 'POST') {
      try {
        const body = (await req.json().catch(() => ({}))) as {
          projectId?: string;
          recentMessages?: PlanIdeaMessage[];
        };
        const projectId = String(body.projectId || 'local');
        const targetRoot =
          projectId !== 'local'
            ? runtime.listProjects().find((p) => p.id === projectId)?.workspacePath || cwd
            : cwd;
        const result = await generateContextAwarePlanIdeas({
          workspaceRoot: targetRoot,
          recentMessages: Array.isArray(body.recentMessages) ? body.recentMessages : [],
          authToken: extractRequestAuthToken(req),
        });
        return jsonResponse(result);
      } catch (err: any) {
        return errorResponse(err?.message || 'Failed to generate plan ideas.', 500);
      }
    }

    if (url.pathname.startsWith('/api/plans/')) {
      const rawId = decodeURIComponent(url.pathname.slice('/api/plans/'.length));
      const safeId = rawId.replace(/[^a-zA-Z0-9._-]+/g, '_');
      if (!safeId) {
        return errorResponse('Invalid sessionId', 400);
      }
      const plansDir = path.resolve(cwd, '.gram', 'plans');
      const planFile = path.join(plansDir, `${safeId}.md`);
      if (req.method === 'GET') {
        const content = fs.existsSync(planFile) ? fs.readFileSync(planFile, 'utf8') : '';
        return jsonResponse({ sessionId: rawId, content });
      }
      if (req.method === 'POST') {
        const body = (await req.json().catch(() => ({}))) as { content?: string };
        fs.mkdirSync(plansDir, { recursive: true });
        fs.writeFileSync(planFile, String(body.content ?? ''), 'utf8');
        return jsonResponse({ ok: true, path: planFile });
      }
    }

    if (url.pathname === '/api/workspace/diff' || url.pathname === '/api/diff' || url.pathname.startsWith('/api/files/')) {
      const fileResponse = await handleFileRoutes(req, url, runtime, cwd);
      if (fileResponse) return fileResponse;
    }

    if (url.pathname === '/api/task') {
      const runId = url.searchParams.get('runId');
      if (!runId) {
        return errorResponse('Missing runId', 400);
      }
      const tasks = runtime.listRecentTasks(50);
      const matched = tasks.find(t => t.runId === runId);
      const checkpoint = runtime.getCheckpoint(matched?.sessionId || 'default', runId);
      const layerId = checkpoint?.activeLayerId || 'main';
      const diffs = runtime.gramfs.listModifiedFiles(layerId).map((entry) => {
        const hostPath = path.resolve(cwd, entry.path);
        const before = entry.originalHash && fs.existsSync(hostPath) ? fs.readFileSync(hostPath, 'utf8') : '';
        return { path: entry.path, isDeleted: entry.isDeleted, patch: formatUnifiedDiff(entry.path, before, entry.isDeleted ? '' : entry.content), content: entry.isDeleted ? undefined : entry.content };
      });
      return jsonResponse(checkpoint ? { ...checkpoint, diffs } : null);
    }

    if (url.pathname === '/api/task/delete' && req.method === 'POST') {
      const body = await req.json();
      if (!body.sessionId || !body.runId) {
        return errorResponse('Missing sessionId or runId', 400);
      }
      runtime.deleteCheckpoint(body.sessionId, body.runId);
      return jsonResponse({ deleted: true, runId: body.runId });
    }

    if (url.pathname === '/api/task/approve' && req.method === 'POST') {
      const body = await req.json();
      if (!body.runId) {
        return errorResponse('Missing runId', 400);
      }
      const tasks = runtime.listRecentTasks(50);
      const matched = tasks.find((t) => t.runId === body.runId);
      const sessionId = body.sessionId || matched?.sessionId || 'default';
      const updated = await runtime.respondApproval(sessionId, body.runId, Boolean(body.approved));
      if (!updated) {
        return errorResponse('Task not found', 404);
      }
      void syncWithLyraHub(req);
      return jsonResponse({ ok: true, checkpoint: updated });
    }

    if (url.pathname.startsWith('/api/projects')) {
      const projRes = await handleProjectRoutes(req, url, runtime);
      if (projRes) return projRes;
    }

    if (url.pathname === '/api/open-external' || url.pathname.startsWith('/api/processes')) {
      const sysRes = await handleSystemRoutes(req, url);
      if (sysRes) return sysRes;
    }

    const buildSystemChecks = async () => {
      const rawCookie = req.headers.get('cookie') || '';
      const cookieMatch = rawCookie.match(/(?:^|;\s*)gram_session=([^;]+)/);
      const localToken = cookieMatch ? decodeURIComponent(cookieMatch[1]) : undefined;
      const lyraToken = extractCookieValue(rawCookie, 'gram_lyra_token');
      const internetCheckPromise = fetch('https://cp.cloudflare.com/generate_204', {
        method: 'HEAD',
        signal: AbortSignal.timeout(1200),
      })
        .then((r) => r.ok || r.status === 204)
        .catch(() => false);
      const [lyraState, providers, internetConnected] = await Promise.all([
        fetchLyraAuthState(req),
        fetchLyraAuthProviders(),
        internetCheckPromise,
      ]);
      const localUser = runtime.getUserBySessionToken(localToken);
      if (lyraState.connected && lyraState.authenticated && lyraState.user && lyraToken) {
        runtime.cacheHubSession(lyraToken, lyraState.user);
      } else if (lyraState.connected && !lyraState.authenticated && lyraToken) {
        runtime.invalidateCachedHubSession(lyraToken);
      }
      const cachedHub = !lyraState.connected && lyraToken ? runtime.getCachedHubSession(lyraToken) : null;
      const auth =
        lyraState.connected && lyraState.authenticated && lyraState.user
          ? {
              connected: true,
              authenticated: true,
              user: lyraState.user,
              source: 'lyra',
              lyraConnected: true,
              googleEnabled: providers.googleEnabled,
              hubUrl: lyraState.hubUrl,
            }
          : cachedHub
          ? {
              connected: false,
              authenticated: true,
              user: cachedHub.user,
              source: 'lyra-cached',
              lyraConnected: false,
              googleEnabled: providers.googleEnabled,
              hubUrl: lyraState.hubUrl,
            }
          : {
              connected: Boolean(localUser) || lyraState.connected,
              authenticated: Boolean(localUser),
              user: localUser,
              source: localUser ? 'local' : lyraState.connected ? 'lyra' : 'local',
              lyraConnected: lyraState.connected,
              googleEnabled: providers.googleEnabled,
              hubUrl: lyraState.hubUrl,
            };
      const byokModels = runtime.byok.listPublicModels().map((m) => ({
        id: m.id,
        name: m.name,
        provider: m.provider,
        modelId: m.modelId,
        baseUrl: m.baseUrl,
        hasApiKey: m.hasApiKey,
        source: 'byok' as const,
      }));
      const catalogModels = listAllowedModels().map((m) => ({
        ...m,
        source: m.provider === 'openrouter' ? ('openrouter' as const) : ('builtin' as const),
      }));
      const projects = runtime.listProjects();
      const workspaceExists = fs.existsSync(cwd);
      const hasProviderKey = Boolean(
        process.env.OPENROUTER_API_KEY ||
          process.env.ANTHROPIC_API_KEY ||
          process.env.OPENAI_API_KEY ||
          process.env.GEMINI_API_KEY ||
          process.env.AWS_ACCESS_KEY_ID ||
          process.env.AWS_PROFILE ||
          byokModels.some((m) => m.hasApiKey)
      );
      const requireAuth = process.env.LYRA_REQUIRE_AUTH === 'true';
      const tokenExpired =
        (Boolean(localToken) && !localUser) || (Boolean(lyraToken) && lyraState.connected && !lyraState.authenticated);

      const systemChecks = [
        {
          id: 'workspace_storage',
          label: 'Workspace & storage',
          severity: 'critical' as const,
          ok: workspaceExists && Array.isArray(projects),
          message: workspaceExists
            ? 'Workspace directory and local store are ready.'
            : `Workspace directory is missing or inaccessible: ${cwd}`,
          action: 'retry' as const,
        },
        {
          id: 'auth_required',
          label: 'Authentication',
          severity: requireAuth ? ('critical' as const) : ('warning' as const),
          ok: auth.authenticated && !tokenExpired,
          message: tokenExpired
            ? 'Your login session token is expired or invalid. Please sign in again.'
            : auth.source === 'lyra-cached'
              ? 'Signed in (offline cached session — will re-sync when Lyra Hub is reachable).'
              : auth.authenticated
                ? 'Signed in and session token is valid.'
                : 'Not signed in. Cloud sync and Lyra Hub features require signing in.',
          action: 'signin' as const,
        },
        {
          id: 'internet_connection',
          label: 'Internet connection',
          severity: 'warning' as const,
          ok: internetConnected,
          message: internetConnected
            ? 'Connected to the internet.'
            : 'No internet connection detected. Cloud models, web search, and cloud sync may not work.',
          action: 'retry' as const,
        },
        {
          id: 'lyra_hub',
          label: 'Lyra Hub',
          severity: 'warning' as const,
          ok: lyraState.connected,
          message: lyraState.connected
            ? 'Connected to Lyra Hub.'
            : 'Lyra Hub is unreachable. Remote swarms and cross-device sync are unavailable.',
          action: 'retry' as const,
        },
        {
          id: 'ai_providers',
          label: 'AI model providers',
          severity: 'warning' as const,
          ok: hasProviderKey || catalogModels.length > 0,
          message:
            hasProviderKey || catalogModels.length > 0
              ? 'AI model providers are available.'
              : 'No AI provider API keys configured. Add a BYOK key in Settings to run models.',
          action: 'settings' as const,
        },
      ];
      return { auth, byokModels, catalogModels, projects, systemChecks };
    };

    if (url.pathname === '/api/system/checks' && req.method === 'GET') {
      const { systemChecks, auth } = await buildSystemChecks();
      return jsonResponse({ systemChecks, auth });
    }

    if (url.pathname === '/api/bootstrap' && req.method === 'GET') {
      const requestedSessionId = url.searchParams.get('sessionId') || '';
      const { auth, byokModels, catalogModels, projects, systemChecks } = await buildSystemChecks();

      const testSessions = new Set(['sess-1', 'sess-2', 'sess-3', 's-sync', 's-async', 's-resume', 'test-session']);
      const tasks = runtime.listRecentTasks(30).filter((t) => !testSessions.has(t.sessionId));
      const chats = runtime.listChatSessions(200);
      const activeChat = requestedSessionId ? runtime.getChatSession(requestedSessionId) : null;
      let activePlan = '';
      if (requestedSessionId) {
        const safePlanId = requestedSessionId.replace(/[^a-zA-Z0-9._-]+/g, '_');
        const planPath = path.resolve(cwd, '.gram', 'plans', `${safePlanId}.md`);
        if (fs.existsSync(planPath)) {
          try {
            activePlan = fs.readFileSync(planPath, 'utf8');
          } catch {}
        }
      }

      return jsonResponse({
        auth,
        projects,
        chats,
        activeChat,
        activePlan,
        models: {
          defaultModelId: AUTO_MODEL_ID,
          openRouterEnabled: Boolean(process.env.OPENROUTER_API_KEY),
          models: [...byokModels, ...catalogModels],
        },
        tasks,
        processes: sharedProcessManager.list(),
        devices: connect.listOnlineDevices(),
        systemChecks,
      });
    }

    if (url.pathname === '/api/chats' && req.method === 'GET') {
      const modeParam = url.searchParams.get('mode');
      const mode = modeParam === 'chat' || modeParam === 'code' ? modeParam : undefined;
      const chats = runtime.listChatSessions(200, mode);
      return jsonResponse(chats);
    }

    if (url.pathname === '/api/chats/archived' && req.method === 'GET') {
      return jsonResponse(runtime.listArchivedChatSessions(50));
    }

    if (url.pathname === '/api/chat' && req.method === 'GET') {
      const sessionId = url.searchParams.get('sessionId');
      if (!sessionId) {
        return errorResponse('Missing sessionId', 400);
      }
      const chat = runtime.getChatSession(sessionId);
      return jsonResponse(chat);
    }

    if (url.pathname === '/api/chat/save' && req.method === 'POST') {
      const body = await req.json();
      if (!body.sessionId) {
        return errorResponse('Missing sessionId', 400);
      }
      runtime.saveChatSession({
        sessionId: body.sessionId,
        title: body.title || 'Untitled chat',
        projectId: body.projectId,
        projectLabel: body.projectLabel,
        modelId: body.modelId,
        mode: body.mode === 'code' || body.mode === 'plan' ? 'code' : 'chat',
        messages: Array.isArray(body.messages) ? body.messages : [],
      });
      void syncWithLyraHub(req);
      return jsonResponse({ ok: true });
    }

    if (url.pathname === '/api/chat/title' && req.method === 'POST') {
      const body = await req.json().catch(() => null);
      if (!body || typeof body.prompt !== 'string' || !body.prompt.trim()) {
        return errorResponse('A prompt is required to generate a chat title.', 400);
      }
      try {
        const title = await runtime.generateChatTitle({
          prompt: body.prompt,
          modelId: typeof body.modelId === 'string' ? body.modelId : 'auto',
          authToken: extractRequestAuthToken(req),
        });
        return jsonResponse({ title });
      } catch (error: any) {
        return errorResponse(error?.message || 'Could not generate a chat title.', 502);
      }
    }

    if (url.pathname === '/api/chat/archive' && req.method === 'POST') {
      const body = await req.json();
      if (!body.sessionId) {
        return errorResponse('Missing sessionId', 400);
      }
      const archived = body.archived !== false;
      runtime.archiveChatSession(body.sessionId, archived);
      void syncWithLyraHub(req);
      return jsonResponse({ ok: true, archived, sessionId: body.sessionId });
    }

    if (url.pathname === '/api/chat/delete' && req.method === 'POST') {
      const body = await req.json();
      if (!body.sessionId) {
        return errorResponse('Missing sessionId', 400);
      }
      // Default to soft-archive (30-day restore window) instead of hard deletion
      runtime.archiveChatSession(body.sessionId, true);
      void syncWithLyraHub(req);
      return jsonResponse({ archived: true, sessionId: body.sessionId });
    }

    if (url.pathname === '/api/chat/fork' && req.method === 'POST') {
      try {
        const body = (await req.json()) as {
          sessionId?: string;
          messageId?: string;
          modelId?: string;
          mode?: 'chat' | 'code';
          title?: string;
        };
        if (!body.sessionId) return errorResponse('sessionId is required', 400);
        const latestCheckpoint = runtime.listRecentTasks(50).find((cp) => cp.sessionId === body.sessionId);
        const forked = runtime.forkChatSession({
          sourceSessionId: String(body.sessionId),
          messageId: body.messageId ? String(body.messageId) : undefined,
          targetModelId: body.modelId ? String(body.modelId) : undefined,
          targetMode: body.mode === 'code' || body.mode === 'chat' ? body.mode : undefined,
          title: body.title ? String(body.title) : undefined,
          baseCheckpointRunId: latestCheckpoint?.runId,
        });
        if (!forked) return errorResponse('Source chat session not found', 404);
        void syncWithLyraHub(req);
        return jsonResponse({ session: forked });
      } catch (err: any) {
        return errorResponse(err?.message || 'Could not fork chat session.', 400);
      }
    }

    if (url.pathname === '/api/chat/handoff' && req.method === 'POST') {
      try {
        const body = (await req.json()) as {
          sessionId?: string;
          fromModelId?: string;
          toModelId?: string;
          reason?: string;
          messages?: Array<{ role?: string; content?: string; id?: string }>;
        };
        if (!body.toModelId) return errorResponse('toModelId is required', 400);
        const sessionId = body.sessionId ? String(body.sessionId) : `handoff-${Date.now()}`;
        const saved = body.sessionId ? runtime.getChatSession(sessionId) : null;
        const fromModelId = String(body.fromModelId || saved?.modelId || 'auto');
        const envelope = new ContextEnvelope({
          sessionId,
          workspacePath: cwd,
          activeModel: fromModelId,
          activeFamily: ContextEnvelope.detectFamily(fromModelId),
          pinnedFacts: runtime.getLatestPinnedFacts(body.sessionId ? sessionId : undefined),
        });
        const sourceMessages = Array.isArray(body.messages)
          ? body.messages
          : Array.isArray(saved?.messages)
          ? (saved!.messages as Array<{ role?: string; content?: string }>)
          : [];
        for (const msg of sourceMessages.slice(-8)) {
          if (msg && typeof msg.content === 'string' && msg.content.trim()) {
            envelope.appendTurn({
              role: msg.role === 'assistant' ? 'assistant' : 'user',
              content: msg.content.trim().slice(0, 2000),
            });
          }
        }
        const capsule = envelope.buildHandoffCapsule(String(body.toModelId), body.reason || 'user_handoff');
        if (saved) {
          const handoffNotice = {
            id: `handoff-${Date.now()}`,
            role: 'assistant',
            content: `Handed off from \`${capsule.fromModelId}\` to \`${capsule.toModelId}\` (${capsule.reason}).`,
            handoff: capsule,
          };
          runtime.saveChatSession({
            sessionId: saved.sessionId,
            title: saved.title,
            projectId: saved.projectId,
            projectLabel: saved.projectLabel,
            modelId: capsule.toModelId,
            mode: saved.mode,
            messages: [...saved.messages, handoffNotice],
          });
          void syncWithLyraHub(req);
        }
        return jsonResponse({ capsule });
      } catch (err: any) {
        return errorResponse(err?.message || 'Could not build model handoff.', 400);
      }
    }

    if (url.pathname === '/api/sync' && req.method === 'POST') {
      const syncResult = await syncWithLyraHub(req);
      return jsonResponse({
        ok: true,
        ...syncResult,
        chats: runtime.listChatSessions(200),
        tasks: runtime.listRecentTasks(50),
        projects: runtime.listProjects(),
        conflicts: runtime.listUnresolvedSyncConflicts(),
      });
    }

    if (url.pathname === '/api/sync/conflicts' && req.method === 'GET') {
      return jsonResponse(runtime.listUnresolvedSyncConflicts());
    }

    if (url.pathname === '/api/sync/conflicts/resolve' && req.method === 'POST') {
      const body = await req.json().catch(() => null);
      if (!body?.conflictId || (body.choice !== 'local' && body.choice !== 'remote')) {
        return errorResponse('conflictId and a local/remote choice are required.', 400);
      }
      const resolved = runtime.resolveSyncConflict(String(body.conflictId), body.choice);
      return resolved
        ? jsonResponse({ ok: true, conflicts: runtime.listUnresolvedSyncConflicts() })
        : errorResponse('Sync conflict not found.', 404);
    }

    if (url.pathname === '/api/history/clear' && req.method === 'POST') {
      runtime.clearAllHistory();
      return jsonResponse({ cleared: true });
    }

    if (url.pathname === '/api/cancel' && req.method === 'POST') {
      const body = await req.json();
      const cancelled = runtime.cancelRun(body.runId);
      return jsonResponse({ cancelled, runId: body.runId });
    }

    if (url.pathname === '/api/skills' && req.method === 'GET') {
      const skills = runtime.skills.listSkills().map((s) => ({
        name: s.name,
        displayName: s.displayName,
        description: s.description,
        whenToUse: s.whenToUse,
      }));
      return jsonResponse({ skills });
    }

    if (url.pathname === '/api/doctor') {
      const doc = await runtime.tools.envSandboxDoctor();
      return jsonResponse(doc);
    }

    if (url.pathname === '/api/connect' && req.method === 'GET') {
      connect.recordHeartbeat({
        deviceId: 'studio-local',
        deviceName: `Local (${os.hostname()})`,
        deviceType: 'desktop',
        workspacePath: cwd,
        isExecutionHost: true,
      });
      const online = connect.listOnlineDevices();
      const devices = online.map((d) => ({
        ...d,
        kind: d.deviceId === 'studio-local' ? 'local' : d.deviceType === 'cloud' ? 'cloud' : 'network',
      }));
      const hasCloudConfig = Boolean(
        process.env.LYRA_API_URL || process.env.AWS_ACCESS_KEY_ID || process.env.AWS_PROFILE
      );
      if (hasCloudConfig && !devices.some((d) => d.kind === 'cloud')) {
        devices.push({
          deviceId: 'cloud-bedrock',
          deviceName: 'Cloud (Bedrock)',
          deviceType: 'cloud',
          kind: 'cloud',
          workspacePath: undefined,
          activeSessionId: undefined,
          isExecutionHost: true,
          lastSeenMs: Date.now(),
        });
      }
      return jsonResponse({ devices });
    }

    if (url.pathname === '/api/connect/heartbeat' && req.method === 'POST') {
      const body = await req.json() as {
        deviceId?: string;
        deviceName?: string;
        deviceType?: 'cli' | 'desktop' | 'web' | 'mobile' | 'cloud' | 'network';
        workspacePath?: string;
        activeSessionId?: string;
        isExecutionHost?: boolean;
      };
      if (!body.deviceId) {
        return errorResponse('deviceId is required', 400);
      }
      const recorded = connect.recordHeartbeat({
        deviceId: body.deviceId,
        deviceName: body.deviceName,
        deviceType: body.deviceType || 'network',
        workspacePath: body.workspacePath,
        activeSessionId: body.activeSessionId,
        isExecutionHost: body.isExecutionHost ?? true,
      });
      return jsonResponse({ ok: true, device: recorded });
    }

    if (url.pathname === '/api/code-mode' && req.method === 'POST') {
      const body = await req.json();
      const res = await runtime.codeMode.execute(body.script || '');
      return jsonResponse(res);
    }

    const isAuthorizedModelId = (modelId: string): boolean =>
      isAllowedModel(modelId) || Boolean(runtime.byok.getModel(modelId));

    if (url.pathname === '/api/models' || url.pathname.startsWith('/api/models/')) {
      const modelResponse = await handleModelRoutes(req, url, runtime);
      if (modelResponse) return modelResponse;
    }

    if (url.pathname.startsWith('/api/auth/')) {
      const authResponse = await handleAuthRoutes(req, url, runtime, syncWithLyraHub);
      if (authResponse) return authResponse;
    }

    if (url.pathname === '/api/voice/transcribe' && req.method === 'POST') {
      const contentLength = Number(req.headers.get('content-length') || 0);
      if (contentLength > 25 * 1024 * 1024) {
        return errorResponse('Audio exceeds the 25 MB upload limit.', 413);
      }
      try {
        const audio = await readAudioBody(req);
        const provider = url.searchParams.get('provider') || req.headers.get('x-stt-provider') || undefined;
        const result = await transcribeAudio(audio, req.signal, { provider });
        return jsonResponse(result);
      } catch (error: any) {
        const status = error?.name === 'AbortError' ? 499 : /not configured|not available|not installed/i.test(String(error?.message || '')) ? 503 : 502;
        return errorResponse(error?.message || 'Transcription failed.', status);
      }
    }

    if (url.pathname === '/api/voice/voices' && req.method === 'GET') {
      try {
        const provider = url.searchParams.get('provider') || undefined;
        const result = await listAvailableVoices(req.signal, provider);
        return jsonResponse(result);
      } catch (error: any) {
        return errorResponse(error?.message || 'Could not load voices.', 502);
      }
    }

    if (url.pathname === '/api/voice/speak' && req.method === 'POST') {
      try {
        const body = (await req.json()) as {
          text?: string;
          provider?: string;
          voiceId?: string;
          modelId?: string;
          stability?: number;
          similarityBoost?: number;
          speed?: number;
        };
        const response = await synthesizeSpeech(String(body.text || ''), req.signal, {
          provider: body.provider,
          voiceId: body.voiceId,
          modelId: body.modelId,
          stability: body.stability,
          similarityBoost: body.similarityBoost,
          speed: body.speed,
        });
        return new Response(response.body, {
          headers: { 'Content-Type': response.headers.get('content-type') || 'audio/mpeg', 'Cache-Control': 'no-store' },
        });
      } catch (error: any) {
        const status = error?.name === 'AbortError' ? 499 : /must be configured|not configured|not available|only available/i.test(String(error?.message || '')) ? 503 : 502;
        return errorResponse(error?.message || 'Speech generation failed.', status);
      }
    }

    if (url.pathname.startsWith('/api/lyra/')) {
      const lyraResponse = await handleLyraRoutes(req, url, runtime, cwd);
      if (lyraResponse) return lyraResponse;
    }

    if (url.pathname === '/api/chat/summarize-handoff' && req.method === 'POST') {
      try {
        const body = await req.json() as {
          messages?: Array<{ role?: string; content?: string }>;
          goal?: string;
        };
        const rawMessages = Array.isArray(body.messages) ? body.messages : [];
        const transcript = rawMessages
          .filter((m) => m && typeof m.content === 'string' && m.content.trim())
          .slice(-12)
          .map((m) => `${m.role === 'assistant' ? 'Assistant' : 'User'}: ${m.content!.trim().slice(0, 1500)}`)
          .join('\n\n');

        const fallbackSummary = [
          body.goal ? `**Goal:** ${body.goal.trim()}` : '',
          transcript ? `**Context from Chat:**\n${rawMessages.slice(-4).map((m) => `- **${m.role === 'assistant' ? 'Assistant' : 'User'}:** ${(m.content || '').trim().slice(0, 280)}`).join('\n')}` : '',
        ].filter(Boolean).join('\n\n') || 'Continue the previous conversation in a coding workspace.';

        if (!transcript) {
          return jsonResponse({ summary: fallbackSummary });
        }

        try {
          const economyModel = selectOptimalModel({ taskComplexity: 'simple', priority: 'cost' });
          const turn = await streamLlmTurn({
            model: economyModel,
            systemPrompt: 'Summarize the following chat into a concise coding handoff (Goal, Key Decisions/Constraints, Files/Tech Mentioned, Suggested Next Step). Keep it under 180 words in clean Markdown.',
            messages: [{ role: 'user', content: transcript }],
            tools: [],
            effort: 'low',
            authToken: extractRequestAuthToken(req),
            signal: AbortSignal.timeout(10_000),
          });
          const summary = turn.text?.trim() || fallbackSummary;
          return jsonResponse({ summary, modelId: economyModel.id });
        } catch {
          return jsonResponse({ summary: fallbackSummary });
        }
      } catch (err: any) {
        return errorResponse(err?.message || 'Could not summarize chat handoff.', 400);
      }
    }

    if (url.pathname === '/api/chat/summarize' && req.method === 'POST') {
      try {
        const body = (await req.json().catch(() => ({}))) as {
          sessionId?: string;
          messages?: Array<{ role?: string; content?: string }>;
        };
        let rawMessages = Array.isArray(body.messages) ? body.messages : [];
        if (rawMessages.length === 0 && body.sessionId) {
          const saved = runtime.getChatSession(String(body.sessionId));
          if (saved && Array.isArray(saved.messages)) {
            rawMessages = saved.messages as Array<{ role?: string; content?: string }>;
          }
        }
        const transcript = rawMessages
          .filter((m) => m && typeof m.content === 'string' && m.content.trim())
          .slice(-24)
          .map((m) => `${m.role === 'assistant' ? 'Assistant' : 'User'}: ${m.content!.trim().slice(0, 1800)}`)
          .join('\n\n');

        if (!transcript) {
          return jsonResponse({ summary: 'No messages to summarize in this chat yet.', source: 'empty' });
        }

        const fallbackSummary = rawMessages
          .filter((m) => m && typeof m.content === 'string' && m.content.trim())
          .slice(-6)
          .map((m) => `${m.role === 'assistant' ? 'Assistant' : 'User'}: ${m.content!.trim().slice(0, 320)}`)
          .join('\n\n');

        try {
          const economyModel = selectOptimalModel({ taskComplexity: 'simple', priority: 'cost' });
          const turn = await streamLlmTurn({
            model: economyModel,
            systemPrompt:
              'Summarize the conversation details so far into a concise, clear summary (as long as needed, up to 5 paragraphs maximum). Cover the user goal, key decisions/findings, files or tools involved, and current state or next steps.',
            messages: [{ role: 'user', content: transcript }],
            tools: [],
            effort: 'low',
            authToken: extractRequestAuthToken(req),
            signal: AbortSignal.timeout(12_000),
          });
          const summary = turn.text?.trim() || fallbackSummary;
          return jsonResponse({ summary, modelId: economyModel.id, source: 'llm' });
        } catch {
          return jsonResponse({ summary: fallbackSummary, source: 'fallback' });
        }
      } catch (err: any) {
        return errorResponse(err?.message || 'Could not summarize chat.', 400);
      }
    }

    if (url.pathname === '/api/run/stream' && req.method === 'POST') {
      const body = await req.json();
      const mode: 'chat' | 'plan' | 'code' =
        body.mode === 'chat' ? 'chat' : body.mode === 'plan' ? 'plan' : 'code';
      const promptText = String(body.prompt || '');
      if (
        body.projectId &&
        body.projectId !== 'local' &&
        /\b(explain|describe|summarize|scan|what (is|does) (this|the) project|this project is)\b/i.test(promptText)
      ) {
        void runtime.summarizeProjectInBackground(String(body.projectId), promptText);
      }
      const requestedModel = body.modelId || AUTO_MODEL_ID;
      const useDirectLlm = process.env.GRAM_DIRECT_LLM === 'true';
      const resolvedModel =
        requestedModel !== AUTO_MODEL_ID && isAuthorizedModelId(requestedModel)
          ? requestedModel
          : !useDirectLlm
            ? AUTO_MODEL_ID
            : selectOptimalModel({
                taskComplexity: mode === 'chat' ? 'simple' : 'moderate',
                priority: mode === 'chat' ? 'cost' : mode === 'plan' ? 'planning' : 'balanced',
              }).id;

      const encoder = new TextEncoder();
      const stream = new ReadableStream({
        async start(controller) {
          const send = (event: Record<string, unknown>) => {
            try {
              controller.enqueue(encoder.encode(`data: ${JSON.stringify(event)}\n\n`));
            } catch {}
          };

          try {
            const result = await runtime.executeRun({
              sessionId: body.sessionId || `session-${Date.now()}`,
              runId: body.runId,
              prompt: body.prompt || '',
              modelId: resolvedModel,
              requestedModelId: requestedModel,
              mode,
              history: Array.isArray(body.history) ? body.history : undefined,
              systemPrompt: typeof body.systemPrompt === 'string' ? body.systemPrompt : undefined,
              effort: body.effort,
              projectId: body.projectId,
              projectLabel: body.projectLabel,
              kind: body.kind,
              requireApproval: body.requireApproval,
              isolateWorktree: Boolean(body.isolateWorktree),
              authToken: extractRequestAuthToken(req),
              signal: req.signal,
              lyra: createLyraToolBridge(req),
              onEvent: (evt) => send(evt),
            });
            void syncWithLyraHub(req);
            send({ type: 'done', result: { ...result, resolvedModelId: resolvedModel } });
          } catch (err: any) {
            send({ type: 'error', error: err?.message || 'Execution failed', diagnostics: err?.diagnostics });
          } finally {
            try {
              controller.close();
            } catch {}
          }
        },
      });

      return new Response(stream, {
        headers: {
          'Content-Type': 'text/event-stream; charset=utf-8',
          'Cache-Control': 'no-cache, no-transform',
          Connection: 'keep-alive',
        },
      });
    }

    if (url.pathname === '/api/run' && req.method === 'POST') {
      const body = await req.json();
      const requestedModel = body.modelId || AUTO_MODEL_ID;
      if (!isAuthorizedModelId(requestedModel)) {
        return errorResponse(`Model ${requestedModel} is not in the verified coding allowlist.`, 400);
      }
      const resolvedModel =
        requestedModel === AUTO_MODEL_ID && process.env.GRAM_DIRECT_LLM === 'true'
          ? selectOptimalModel({ taskComplexity: 'moderate', priority: 'balanced' }).id
          : requestedModel;
      const result = await runtime.executeRun({
        sessionId: body.sessionId || `session-${Date.now()}`,
        runId: body.runId,
        prompt: body.prompt || '',
        modelId: resolvedModel,
        requestedModelId: requestedModel,
        history: Array.isArray(body.history) ? body.history : undefined,
        systemPrompt: typeof body.systemPrompt === 'string' ? body.systemPrompt : undefined,
        effort: body.effort,
        projectId: body.projectId,
        projectLabel: body.projectLabel,
        kind: body.kind,
        requireApproval: body.requireApproval,
        authToken: extractRequestAuthToken(req),
        signal: req.signal,
        lyra: createLyraToolBridge(req),
      });
      void syncWithLyraHub(req);
      return jsonResponse({ ...result, resolvedModelId: resolvedModel });
    }

    if (url.pathname === '/api/diagnostics/errors' && req.method === 'GET') {
      const diagFile = path.join(cwd, '.gram', 'diagnostics', 'errors.jsonl');
      let entries: unknown[] = [];
      try {
        if (fs.existsSync(diagFile)) {
          const lines = fs.readFileSync(diagFile, 'utf8').trim().split('\n').filter(Boolean);
          entries = lines.slice(-50).reverse().map((line) => {
            try {
              return JSON.parse(line);
            } catch {
              return { raw: line };
            }
          });
        }
      } catch {}
      return jsonResponse({ errors: entries, file: diagFile });
    }

    if (url.pathname === '/api/rpc' && req.method === 'POST') {
      const body = await req.json();
      const rpcRes = ahp.handleRpc(body);
      return jsonResponse(rpcRes);
    }

    return new Response('Not Found', { status: 404 });
}

export const server = process.env.NODE_ENV === 'test'
  ? { fetch: handleRequest, stop: () => runtime.close() }
  : Bun.serve({ port: PORT, hostname: process.env.GRAM_HOST || '127.0.0.1', idleTimeout: 255, fetch: handleRequest });

if (process.env.NODE_ENV !== 'test') {
  console.log(`\n[Gram v2] Server listening on http://localhost:${(server as any).port}`);
  console.log(`          Workspace: ${cwd}\n`);
}

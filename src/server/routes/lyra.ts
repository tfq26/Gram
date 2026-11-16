/**
 * Lyra Hub proxy, cloud swarm diff pull/commit/rollback, and escalation routes.
 */

import fs from 'node:fs';
import path from 'node:path';
import type { GramAgentRuntime } from '../../runtime/agentRuntime';
import { formatUnifiedDiff } from '../../runtime/agentTools';
import { applyUnifiedFilePatch } from '../../runtime/unifiedPatch';
import { isAllowedModel } from '../../providers/router';
import { errorResponse, jsonResponse } from '../http';
import { fetchLyraAuthState, proxyLyraJson, proxyLyraStream } from '../lyraClient';

const SECRET_PATH_PATTERN = /(^|\/)\.env(?:\.|$)|(^|\/)(?:secrets?|credentials?)(?:\/|\.|$)|\.(?:pem|key)$/i;

export async function handleLyraRoutes(
  req: Request,
  url: URL,
  runtime: GramAgentRuntime,
  cwd: string
): Promise<Response | null> {
  if (url.pathname === '/api/lyra/auth/state' && req.method === 'GET') {
    const state = await fetchLyraAuthState(req);
    return jsonResponse(state);
  }

  if (url.pathname === '/api/lyra/auth/sign-in' && req.method === 'POST') {
    const body = await req.json();
    return proxyLyraJson(req, '/api/auth/sign-in/email', { method: 'POST', body });
  }

  if (url.pathname === '/api/lyra/auth/sign-out' && req.method === 'POST') {
    return proxyLyraJson(req, '/api/auth/sign-out', { method: 'POST', body: {} });
  }

  if (url.pathname === '/api/lyra/sessions' && req.method === 'GET') {
    return proxyLyraJson(req, '/api/sessions', { method: 'GET' });
  }

  if (url.pathname === '/api/lyra/chat' && req.method === 'POST') {
    const body = (await req.json()) as Record<string, any>;
    if (body.pinnedModelId && !isAllowedModel(body.pinnedModelId)) {
      return errorResponse(`Model ${body.pinnedModelId} is not in the verified allowlist.`, 400);
    }
    return proxyLyraJson(req, '/api/ahp/chat', { method: 'POST', body });
  }

  if (url.pathname === '/api/lyra/chat/stream' && req.method === 'POST') {
    const body = (await req.json()) as Record<string, any>;
    if (body.pinnedModelId && !isAllowedModel(body.pinnedModelId)) {
      return errorResponse(`Model ${body.pinnedModelId} is not in the verified allowlist.`, 400);
    }
    return proxyLyraStream(req, body);
  }

  if (url.pathname === '/api/lyra/swarm/status' && req.method === 'GET') {
    const runId = url.searchParams.get('runId') || '';
    const sessionId = url.searchParams.get('sessionId') || '';
    if (!runId) return errorResponse('Missing runId.', 400);
    return proxyLyraJson(
      req,
      `/api/gateway/runs/${encodeURIComponent(runId)}/status?sessionId=${encodeURIComponent(sessionId)}`,
      { method: 'GET' }
    );
  }

  if (url.pathname === '/api/lyra/swarm/pull' && req.method === 'POST') {
    try {
      const body = (await req.json()) as { runId?: string; sessionId?: string };
      const runId = String(body.runId || '').trim();
      const sessionId = String(body.sessionId || '').trim();
      if (!runId) return errorResponse('Missing runId.', 400);

      const statusResponse = await proxyLyraJson(
        req,
        `/api/gateway/runs/${encodeURIComponent(runId)}/status?sessionId=${encodeURIComponent(sessionId)}`,
        { method: 'GET' }
      );
      const status = (await statusResponse.json()) as any;
      if (!statusResponse.ok) return jsonResponse(status, statusResponse.status);
      if (!/complete|success|succeeded|done/i.test(String(status.state || status.stage || ''))) {
        return jsonResponse({ error: 'Lyra swarm has not completed yet.', status }, 409);
      }

      let diffs: Array<{ filename?: string; path?: string; patch?: string; content?: string; isDeleted?: boolean }> =
        Array.isArray(status.diffs) ? status.diffs : [];
      if (!diffs.length) {
        const diffResponse = await proxyLyraJson(
          req,
          `/api/gateway/runs/${encodeURIComponent(runId)}/diffs?sessionId=${encodeURIComponent(sessionId)}`,
          { method: 'GET' }
        );
        const diffData = (await diffResponse.json()) as any;
        if (!diffResponse.ok) {
          return jsonResponse(
            {
              error: diffData?.error || 'Lyra Hub does not expose completed run diffs yet.',
              requiredEndpoint: `/api/gateway/runs/${runId}/diffs`,
            },
            502
          );
        }
        diffs = Array.isArray(diffData.diffs) ? diffData.diffs : [];
      }
      if (!diffs.length || diffs.length > 250) {
        return errorResponse(diffs.length ? 'Swarm diff exceeds 250 files.' : 'Lyra returned no file diffs.', 422);
      }

      const changes = diffs.map((file) => {
        const patch = String(file.patch || '');
        const pathHint = String(file.filename || file.path || '');
        if (!pathHint || SECRET_PATH_PATTERN.test(pathHint)) {
          throw new Error(`Unsafe or secret file path in swarm patch: ${pathHint || '(missing)'}`);
        }
        if (typeof file.content === 'string') {
          return { path: runtime.gramfs.resolvePath(pathHint), content: file.isDeleted ? null : file.content };
        }
        if (!patch) throw new Error(`Lyra diff for '${pathHint}' has no patch content.`);
        const isNewFile = patch.includes('--- /dev/null');
        const base = isNewFile ? '' : runtime.gramfs.readFile(pathHint, 'main');
        const applied = applyUnifiedFilePatch(patch, base);
        if (SECRET_PATH_PATTERN.test(applied.path)) {
          throw new Error(`Secret file paths cannot be pulled from a swarm patch: ${applied.path}`);
        }
        return { path: runtime.gramfs.resolvePath(applied.path), content: applied.content };
      });

      const layerId = `lyra-${runId.replace(/[^A-Za-z0-9_-]/g, '').slice(0, 64)}`;
      runtime.gramfs.clearLayer(layerId);
      runtime.gramfs.writeFilesAtomic(changes, layerId);
      const resultDiffs = changes.map((change) => {
        let overlayBase = '';
        let hostBase = '';
        try {
          overlayBase = runtime.gramfs.readFile(change.path, 'main');
        } catch {}
        try {
          hostBase = fs.readFileSync(path.resolve(cwd, change.path), 'utf8');
        } catch {}
        const after = change.content || '';
        return {
          path: change.path,
          patch: formatUnifiedDiff(change.path, overlayBase, after),
          hostPatch: formatUnifiedDiff(change.path, hostBase, after),
        };
      });
      return jsonResponse({ ok: true, runId, layerId, diffs: resultDiffs });
    } catch (error: any) {
      return errorResponse(error?.message || 'Could not pull Lyra patch.', 422);
    }
  }

  if (url.pathname === '/api/lyra/swarm/layer/commit' && req.method === 'POST') {
    try {
      const body = (await req.json()) as { layerId?: string };
      const layerId = String(body.layerId || '');
      if (!/^lyra-[A-Za-z0-9_-]{1,64}$/.test(layerId)) {
        return errorResponse('Invalid Lyra overlay layer ID.', 400);
      }
      const changes = runtime.gramfs.listModifiedFiles(layerId);
      if (!changes.length) {
        return errorResponse('This Lyra overlay has no staged changes.', 409);
      }
      const result = runtime.gramfs.commitToHost(layerId);
      return jsonResponse({ ok: true, layerId, ...result });
    } catch (error: any) {
      return errorResponse(error?.message || 'Could not commit the Lyra overlay.', 500);
    }
  }

  if (url.pathname === '/api/lyra/swarm/layer/rollback' && req.method === 'POST') {
    const body = (await req.json()) as { layerId?: string };
    const layerId = String(body.layerId || '');
    if (!/^lyra-[A-Za-z0-9_-]{1,64}$/.test(layerId)) {
      return errorResponse('Invalid Lyra overlay layer ID.', 400);
    }
    runtime.gramfs.clearLayer(layerId);
    return jsonResponse({ ok: true, layerId, rolledBack: true });
  }

  if (url.pathname === '/api/lyra/escalate' && req.method === 'POST') {
    const body = (await req.json()) as Record<string, any>;
    const overlay = runtime.gramfs.listModifiedFiles('main').filter((file) => !SECRET_PATH_PATTERN.test(file.path));
    let remaining = 500_000;
    const workspaceDiff = overlay.slice(0, 100).map((file) => {
      const content = file.isDeleted ? '' : file.content.slice(0, Math.max(0, remaining));
      remaining -= content.length;
      return { path: file.path, content, isDeleted: file.isDeleted, originalHash: file.originalHash };
    });
    const pinnedFacts = body.pinnedFacts || runtime.getLatestPinnedFacts(body.sessionId);
    const snapshot = workspaceDiff
      .map((file) => `### ${file.isDeleted ? 'Deleted' : 'Staged'}: ${file.path}\n${file.isDeleted ? '(deleted)' : file.content}`)
      .join('\n\n');
    const context = [
      body.prompt || '',
      Object.keys(pinnedFacts).length
        ? `## Verified local context\n${Object.entries(pinnedFacts).map(([key, value]) => `- ${key}: ${value}`).join('\n')}`
        : '',
      snapshot ? `## Current GramFS overlay snapshot\n${snapshot}` : '',
    ]
      .filter(Boolean)
      .join('\n\n');
    return proxyLyraJson(req, '/api/ahp/escalate', {
      method: 'POST',
      body: { ...body, prompt: context, workspacePath: cwd, workspaceDiff, pinnedFacts },
    });
  }

  return null;
}

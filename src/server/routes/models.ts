/**
 * Model catalog and BYOK provider configuration routes.
 */

import type { GramAgentRuntime } from '../../runtime/agentRuntime';
import { AUTO_MODEL_ID, getActivePrivacyMode, listAllowedModels, type PrivacyMode } from '../../providers/router';
import { errorResponse, jsonResponse } from '../http';

export async function handleModelRoutes(
  req: Request,
  url: URL,
  runtime: GramAgentRuntime
): Promise<Response | null> {
  if (url.pathname === '/api/settings/privacy' && req.method === 'GET') {
    return jsonResponse({ privacyMode: getActivePrivacyMode() });
  }

  if (url.pathname === '/api/settings/privacy' && req.method === 'POST') {
    try {
      const body = (await req.json()) as { privacyMode?: PrivacyMode };
      const mode = body.privacyMode;
      if (mode !== 'standard' && mode !== 'zdr' && mode !== 'local-only') {
        return errorResponse('privacyMode must be "standard", "zdr", or "local-only".', 400);
      }
      process.env.GRAM_PRIVACY_MODE = mode;
      return jsonResponse({ privacyMode: mode });
    } catch (err: any) {
      return errorResponse(err?.message || 'Failed to update privacy mode.', 400);
    }
  }

  if (url.pathname === '/api/models' && req.method === 'GET') {
    const privacyMode = getActivePrivacyMode();
    const byokModels = runtime.byok
      .listPublicModels()
      .filter((m) => privacyMode !== 'local-only' || m.provider === 'ollama' || Boolean(m.baseUrl?.includes('127.0.0.1') || m.baseUrl?.includes('localhost')))
      .map((m) => ({
        id: m.id,
        name: m.name,
        provider: m.provider,
        modelId: m.modelId,
        baseUrl: m.baseUrl,
        hasApiKey: m.hasApiKey,
        source: 'byok' as const,
      }));
    const catalogModels =
      privacyMode === 'local-only'
        ? []
        : listAllowedModels()
            .filter((m) => privacyMode !== 'zdr' || !m.contributorWarning)
            .map((m) => ({
              ...m,
              source: m.provider === 'openrouter' ? ('openrouter' as const) : ('builtin' as const),
            }));
    return jsonResponse({
      defaultModelId: AUTO_MODEL_ID,
      privacyMode,
      openRouterEnabled: Boolean(process.env.OPENROUTER_API_KEY),
      models: [...byokModels, ...catalogModels],
    });
  }

  if (url.pathname === '/api/models/byok' && req.method === 'GET') {
    return jsonResponse({ models: runtime.byok.listPublicModels() });
  }

  if (url.pathname === '/api/models/byok' && req.method === 'POST') {
    try {
      const body = (await req.json()) as {
        id?: string;
        name?: string;
        provider?: 'openai' | 'anthropic' | 'openrouter' | 'ollama' | 'custom';
        modelId?: string;
        baseUrl?: string;
        apiKey?: string;
      };
      const modelId = String(body.modelId || '').trim();
      if (!modelId) {
        return errorResponse('modelId is required.', 400);
      }
      const saved = runtime.byok.upsertModel({
        id: body.id,
        name: String(body.name || modelId).trim(),
        provider: body.provider || 'openai',
        modelId,
        baseUrl: body.baseUrl,
        apiKey: body.apiKey,
      });
      return jsonResponse({ ok: true, model: saved });
    } catch (err: any) {
      return errorResponse(err?.message || 'Failed to save BYOK model.', 400);
    }
  }

  if (url.pathname.startsWith('/api/models/byok/') && req.method === 'DELETE') {
    const id = decodeURIComponent(url.pathname.slice('/api/models/byok/'.length));
    const deleted = runtime.byok.deleteModel(id);
    return jsonResponse({ ok: deleted }, deleted ? 200 : 404);
  }

  return null;
}

import type { UserContextCategory } from '../../db/userContextStore';
import type { GramAgentRuntime } from '../../runtime/agentRuntime';
import { errorResponse, jsonResponse } from '../http';

const CATEGORIES = new Set<UserContextCategory>([
  'communication', 'coding', 'workflow', 'career', 'family', 'finances',
  'relationships', 'health', 'education', 'background', 'goals', 'other',
]);

export async function handleUserContextRoutes(
  req: Request,
  url: URL,
  runtime: GramAgentRuntime
): Promise<Response | null> {
  if (url.pathname === '/api/user-context/scan-history' && req.method === 'POST') {
    return jsonResponse({ candidates: runtime.scanPastChatsForUserContext() });
  }

  if (url.pathname === '/api/user-context/import-candidate' && req.method === 'POST') {
    const body = await req.json().catch(() => null);
    if (!body || !CATEGORIES.has(body.category) || !body.key || !body.value || !body.evidence || !body.sessionId) {
      return errorResponse('Candidate category, key, value, evidence, and sessionId are required.', 400);
    }
    try {
      const item = runtime.acceptPastChatUserContext({
        category: body.category,
        key: String(body.key),
        value: String(body.value),
        evidence: String(body.evidence),
        sessionId: String(body.sessionId),
        chatTitle: String(body.chatTitle || 'Saved chat'),
      });
      return jsonResponse({ item, profile: runtime.getUserContextProfile() }, 201);
    } catch (error: any) {
      return errorResponse(error?.message || 'Could not import memory candidate.', 400);
    }
  }

  if (url.pathname === '/api/user-context' && req.method === 'GET') {
    return jsonResponse(runtime.getUserContextProfile());
  }

  if (url.pathname === '/api/user-context/settings' && req.method === 'PATCH') {
    const body = await req.json().catch(() => null);
    if (!body || (body.enabled === undefined && body.autoCapture === undefined)) {
      return errorResponse('Provide enabled or autoCapture.', 400);
    }
    return jsonResponse(runtime.updateUserContextSettings({
      enabled: typeof body.enabled === 'boolean' ? body.enabled : undefined,
      autoCapture: typeof body.autoCapture === 'boolean' ? body.autoCapture : undefined,
    }));
  }

  if (url.pathname === '/api/user-context/items' && req.method === 'POST') {
    const body = await req.json().catch(() => null);
    if (!body || !CATEGORIES.has(body.category) || typeof body.value !== 'string') {
      return errorResponse('A valid category and value are required.', 400);
    }
    try {
      const item = runtime.addUserContextItem({ category: body.category, value: body.value });
      return jsonResponse({ item, profile: runtime.getUserContextProfile() }, 201);
    } catch (error: any) {
      return errorResponse(error?.message || 'Could not save personal context.', 400);
    }
  }

  const itemMatch = url.pathname.match(/^\/api\/user-context\/items\/([^/]+)$/);
  if (itemMatch && req.method === 'PUT') {
    const body = await req.json().catch(() => null);
    if (!body || (body.category !== undefined && !CATEGORIES.has(body.category)) || (body.value !== undefined && typeof body.value !== 'string')) {
      return errorResponse('Provide a valid category or value.', 400);
    }
    try {
      const item = runtime.updateUserContextItem(decodeURIComponent(itemMatch[1]!), {
        category: body.category,
        value: body.value,
      });
      if (!item) return errorResponse('Personal context item not found.', 404);
      return jsonResponse({ item, profile: runtime.getUserContextProfile() });
    } catch (error: any) {
      return errorResponse(error?.message || 'Could not update personal context.', 400);
    }
  }

  if (itemMatch && req.method === 'DELETE') {
    const deleted = runtime.deleteUserContextItem(decodeURIComponent(itemMatch[1]!));
    return deleted ? jsonResponse({ ok: true }) : errorResponse('Personal context item not found.', 404);
  }

  if (url.pathname === '/api/user-context' && req.method === 'DELETE') {
    runtime.clearUserContext();
    return jsonResponse({ ok: true, profile: runtime.getUserContextProfile() });
  }

  return null;
}

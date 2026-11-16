/**
 * Managed background process & external URL routes.
 */

import { sharedProcessManager } from '../../runtime/processManager';
import { errorResponse, jsonResponse } from '../http';

export async function handleSystemRoutes(req: Request, url: URL): Promise<Response | null> {
  if (url.pathname === '/api/open-external' && req.method === 'POST') {
    try {
      const body = (await req.json()) as { url?: string };
      const rawUrl = String(body?.url || '').trim();
      let parsed: URL;
      try {
        parsed = new URL(rawUrl);
      } catch {
        return errorResponse('Invalid URL', 400);
      }
      if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:' && parsed.protocol !== 'file:') {
        return errorResponse('Unsupported URL protocol', 400);
      }
      if (process.env.NODE_ENV !== 'test') {
        const cmd =
          process.platform === 'darwin'
            ? ['open', parsed.toString()]
            : process.platform === 'win32'
              ? ['cmd', '/c', 'start', '', parsed.toString()]
              : ['xdg-open', parsed.toString()];
        Bun.spawn(cmd, { stdout: 'ignore', stderr: 'ignore' });
      }
      return jsonResponse({ opened: true, url: parsed.toString() });
    } catch (err: any) {
      return errorResponse(err?.message || 'Could not open URL', 400);
    }
  }

  if (url.pathname === '/api/processes' && req.method === 'GET') {
    return jsonResponse({ processes: sharedProcessManager.list() });
  }

  const procLogsMatch = url.pathname.match(/^\/api\/processes\/([^/]+)\/logs$/);
  if (procLogsMatch && req.method === 'GET') {
    const tail = Number(url.searchParams.get('tail') || 120);
    const data = sharedProcessManager.getLogs(decodeURIComponent(procLogsMatch[1]!), tail);
    if (!data) {
      return errorResponse('Process not found', 404);
    }
    return jsonResponse(data);
  }

  const procStopMatch = url.pathname.match(/^\/api\/processes\/([^/]+)\/stop$/);
  if (procStopMatch && req.method === 'POST') {
    const stopped = await sharedProcessManager.stop(decodeURIComponent(procStopMatch[1]!));
    if (!stopped) {
      return errorResponse('Process not found', 404);
    }
    return jsonResponse({ process: stopped });
  }

  return null;
}

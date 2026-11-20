/**
 * Lyra Hub Client Bridge for Gram
 *
 * Proxies Better Auth, AHP v1.3 chat/escalation, session sync, and cloud swarm
 * status to the configured Lyra Hub (`LYRA_HUB_URL`), degrading gracefully when
 * Gram is running standalone locally.
 */

export interface LyraAuthUser {
  id: string;
  name: string;
  email: string;
}

export interface LyraAuthState {
  connected: boolean;
  authenticated: boolean;
  hubUrl: string;
  user: LyraAuthUser | null;
}

export function getLyraHubUrl(): string {
  return (process.env.LYRA_HUB_URL || 'https://lyra.2helix.org').replace(/\/+$/, '');
}

export function extractCookieValue(cookieHeader: string | null, name: string): string | undefined {
  if (!cookieHeader) return undefined;
  const escaped = name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const match = cookieHeader.match(new RegExp(`(?:^|;\\s*)${escaped}=([^;]+)`));
  return match ? decodeURIComponent(match[1]) : undefined;
}

export function forwardAuthHeaders(req?: Request): Record<string, string> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' };
  if (!req) return headers;
  const cookie = req.headers.get('cookie');
  const auth = req.headers.get('authorization');
  if (cookie) headers['cookie'] = cookie;
  if (auth) {
    headers['authorization'] = auth;
  } else {
    const lyraToken = extractCookieValue(cookie, 'gram_lyra_token');
    if (lyraToken) {
      headers['authorization'] = `Bearer ${lyraToken}`;
    }
  }
  return headers;
}

export async function fetchLyraAuthProviders(): Promise<{
  googleEnabled: boolean;
  signUpEnabled: boolean;
  reachable: boolean;
  error?: string;
}> {
  const hubUrl = getLyraHubUrl();
  try {
    const res = await fetch(`${hubUrl}/api/auth/status`, {
      signal: AbortSignal.timeout(5000),
    });
    if (!res.ok) {
      return {
        googleEnabled: false,
        signUpEnabled: true,
        reachable: false,
        error: `Lyra Hub returned HTTP ${res.status}.`,
      };
    }
    const data = (await res.json()) as { googleEnabled?: boolean; signUpEnabled?: boolean };
    return {
      googleEnabled: Boolean(data?.googleEnabled),
      signUpEnabled: data?.signUpEnabled !== false,
      reachable: true,
    };
  } catch (err: any) {
    return {
      googleEnabled: false,
      signUpEnabled: true,
      reachable: false,
      error: err?.message || `Unable to reach ${hubUrl}.`,
    };
  }
}

export async function fetchLyraAuthState(req?: Request): Promise<LyraAuthState> {
  const hubUrl = getLyraHubUrl();
  try {
    const res = await fetch(`${hubUrl}/api/auth/get-session`, {
      headers: forwardAuthHeaders(req),
      signal: AbortSignal.timeout(2500),
    });
    if (!res.ok) {
      return { connected: true, authenticated: false, hubUrl, user: null };
    }
    const data = (await res.json()) as { user?: LyraAuthUser | null } | null;
    return {
      connected: true,
      authenticated: Boolean(data?.user),
      hubUrl,
      user: data?.user || null,
    };
  } catch {
    return { connected: false, authenticated: false, hubUrl, user: null };
  }
}

export async function proxyLyraJson(
  req: Request,
  pathname: string,
  init?: { method?: string; body?: unknown }
): Promise<Response> {
  const hubUrl = getLyraHubUrl();
  try {
    const res = await fetch(`${hubUrl}${pathname}`, {
      method: init?.method || req.method,
      headers: forwardAuthHeaders(req),
      body: init?.body !== undefined ? JSON.stringify(init.body) : undefined,
      signal: AbortSignal.timeout(15000),
    });
    const text = await res.text();
    const outHeaders = new Headers({ 'Content-Type': res.headers.get('content-type') || 'application/json' });
    const setCookie = res.headers.get('set-cookie');
    if (setCookie) outHeaders.set('set-cookie', setCookie);
    return new Response(text, { status: res.status, headers: outHeaders });
  } catch (err: any) {
    return new Response(
      JSON.stringify({ error: 'Lyra Hub unreachable', detail: err?.message || 'Connection failed', hubUrl }),
      { status: 503, headers: { 'Content-Type': 'application/json' } }
    );
  }
}

function encodeSse(event: Record<string, unknown>): Uint8Array {
  return new TextEncoder().encode(`data: ${JSON.stringify(event)}\n\n`);
}

/** Proxies Lyra's AHP stream while translating its event names into Gram's stream contract. */
export async function proxyLyraStream(req: Request, body: Record<string, unknown>): Promise<Response> {
  const hubUrl = getLyraHubUrl();
  const sessionId = String(body.sessionId || `lyra-session-${Date.now()}`);
  const fallbackRunId = `lyra-${Date.now()}`;
  const stream = new ReadableStream<Uint8Array>({
    async start(controller) {
      const send = (event: Record<string, unknown>) => { try { controller.enqueue(encodeSse(event)); } catch {} };
      send({ type: 'start', runId: fallbackRunId, modelId: String(body.pinnedModelId || 'lyra') });
      try {
        const upstream = await fetch(`${hubUrl}/api/ahp/chat`, {
          method: 'POST',
          headers: { ...forwardAuthHeaders(req), accept: 'text/event-stream' },
          body: JSON.stringify({ ...body, stream: true }),
          signal: req.signal.aborted ? req.signal : AbortSignal.any([req.signal, AbortSignal.timeout(10 * 60_000)]),
        });
        if (!upstream.ok || !upstream.body) {
          const detail = await upstream.text().catch(() => '');
          send({ type: 'error', error: `Lyra Hub returned HTTP ${upstream.status}${detail ? `: ${detail.slice(0, 500)}` : ''}` });
          return;
        }
        const reader = upstream.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';
        let upstreamEvent = 'message';
        let dataLines: string[] = [];
        let finalResult: any = null;
        let streamedText = '';
        const dispatch = () => {
          if (!dataLines.length) { upstreamEvent = 'message'; return; }
          const raw = dataLines.join('\n');
          dataLines = [];
          const eventName = upstreamEvent;
          upstreamEvent = 'message';
          let payload: any;
          try { payload = JSON.parse(raw); } catch { payload = { text: raw }; }
          if (eventName === 'chunk') {
            const delta = String(payload.chunk || payload.text || '');
            streamedText += delta;
            if (delta) send({ type: 'text_delta', delta });
          } else if (eventName === 'status') {
            const status = payload.status;
            if (typeof status === 'string' && /plan|task|worker|swarm/i.test(status)) send({ type: 'task_started', runId: fallbackRunId, title: status.slice(0, 80) });
          } else if (eventName === 'triage') {
            const triage = payload.triage || payload;
            if (triage?.lane === 'full-swarm' || triage?.route === 'full-swarm') send({ type: 'task_started', runId: fallbackRunId, title: String(body.prompt || 'Lyra swarm').slice(0, 64) });
          } else if (eventName === 'done') {
            finalResult = payload;
            const resultText = String(payload.output || payload.text || payload.content || '');
            if (resultText && !streamedText) send({ type: 'text_delta', delta: resultText });
            send({ type: 'done', result: {
              ...payload,
              runId: payload.runId || payload.messageId || fallbackRunId,
              output: resultText || streamedText,
              content: resultText || streamedText,
            } });
          } else if (eventName === 'error') {
            send({ type: 'error', error: String(payload.error || payload.message || 'Lyra execution failed.') });
          } else if (payload.type && ['thinking_delta', 'tool_start', 'tool_result', 'approval_required', 'task_started'].includes(payload.type)) {
            send(payload);
          }
        };
        while (true) {
          const { value, done } = await reader.read();
          if (done) break;
          buffer += decoder.decode(value, { stream: true });
          let boundary: number;
          while ((boundary = buffer.indexOf('\n')) >= 0) {
            const line = buffer.slice(0, boundary).replace(/\r$/, '');
            buffer = buffer.slice(boundary + 1);
            if (line === '') dispatch();
            else if (line.startsWith('event:')) upstreamEvent = line.slice(6).trim();
            else if (line.startsWith('data:')) dataLines.push(line.slice(5).trimStart());
          }
        }
        if (buffer.trim() || dataLines.length) dispatch();
        if (!finalResult) send({ type: 'done', result: { runId: fallbackRunId, output: streamedText, content: streamedText } });
      } catch (error: any) {
        send({ type: 'error', error: error?.name === 'AbortError' ? 'Lyra run cancelled.' : error?.message || 'Lyra Hub unreachable.' });
      } finally {
        try { controller.close(); } catch {}
      }
    },
  });
  return new Response(stream, {
    headers: { 'Content-Type': 'text/event-stream; charset=utf-8', 'Cache-Control': 'no-cache, no-transform', Connection: 'keep-alive' },
  });
}

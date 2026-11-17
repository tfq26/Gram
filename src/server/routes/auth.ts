/**
 * Authentication routes: local SQLite session auth and Lyra Hub OAuth/token handoff.
 */

import type { GramAgentRuntime } from '../../runtime/agentRuntime';
import { errorResponse, jsonResponse } from '../http';
import { fetchLyraAuthProviders, fetchLyraAuthState, getLyraHubUrl, proxyLyraJson } from '../lyraClient';

const pendingDesktopHandoffs = new Map<string, { token?: string; createdAt: number }>();

function pruneDesktopHandoffs() {
  const cutoff = Date.now() - 5 * 60 * 1000;
  for (const [id, entry] of pendingDesktopHandoffs.entries()) {
    if (entry.createdAt < cutoff) pendingDesktopHandoffs.delete(id);
  }
}

function openInSystemBrowser(targetUrl: string) {
  try {
    if (process.platform === 'darwin') {
      Bun.spawn(['open', targetUrl], { stdout: 'ignore', stderr: 'ignore' });
    } else if (process.platform === 'win32') {
      Bun.spawn(['cmd', '/c', 'start', '', targetUrl], { stdout: 'ignore', stderr: 'ignore' });
    } else {
      Bun.spawn(['xdg-open', targetUrl], { stdout: 'ignore', stderr: 'ignore' });
    }
  } catch {}
}

function readSessionCookie(req: Request): string | undefined {
  const rawCookie = req.headers.get('cookie') || '';
  const match = rawCookie.match(/(?:^|;\s*)gram_session=([^;]+)/);
  return match ? decodeURIComponent(match[1]) : undefined;
}

function buildAuthCookie(
  req: Request,
  url: URL,
  name: string,
  value: string,
  maxAgeSeconds = 30 * 24 * 60 * 60
): string {
  const secureFlag =
    url.protocol === 'https:' || req.headers.get('x-forwarded-proto') === 'https' ? '; Secure' : '';
  return `${name}=${encodeURIComponent(value)}; Path=/; HttpOnly; SameSite=Lax; Max-Age=${maxAgeSeconds}${secureFlag}`;
}

export async function handleAuthRoutes(
  req: Request,
  url: URL,
  runtime: GramAgentRuntime,
  syncWithLyraHub: (req?: Request) => Promise<{ synced: boolean; latestSeq: number }>
): Promise<Response | null> {
  if (url.pathname === '/api/auth/state' && req.method === 'GET') {
    const [lyraState, providers] = await Promise.all([
      fetchLyraAuthState(req),
      fetchLyraAuthProviders(),
    ]);
    if (lyraState.connected && lyraState.authenticated && lyraState.user) {
      return jsonResponse({
        connected: true,
        authenticated: true,
        user: lyraState.user,
        source: 'lyra',
        lyraConnected: true,
        googleEnabled: providers.googleEnabled,
        hubUrl: lyraState.hubUrl,
      });
    }
    const localUser = runtime.getUserBySessionToken(readSessionCookie(req));
    return jsonResponse({
      connected: Boolean(localUser) || lyraState.connected,
      authenticated: Boolean(localUser),
      user: localUser,
      source: localUser ? 'local' : lyraState.connected ? 'lyra' : 'local',
      lyraConnected: lyraState.connected,
      googleEnabled: providers.googleEnabled,
      hubUrl: lyraState.hubUrl,
    });
  }

  if ((url.pathname === '/api/auth/handoff' || url.pathname === '/auth/handoff') && req.method === 'GET') {
    const hid = url.searchParams.get('hid') || '';
    const html = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width,initial-scale=1" />
<title>Gram Sign-In</title>
<style>
  body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: #0e0f10; color: #ececec; font-family: -apple-system, BlinkMacSystemFont, "Inter", sans-serif; }
  .card { width: 100%; max-width: 360px; padding: 32px 28px; border-radius: 18px; background: #151719; border: 1px solid rgba(255,255,255,0.1); text-align: center; box-shadow: 0 20px 60px rgba(0,0,0,0.6); }
  .marks { display: inline-flex; align-items: center; justify-content: center; gap: 12px; margin-bottom: 18px; }
  .check { width: 32px; height: 32px; border-radius: 999px; display: inline-flex; align-items: center; justify-content: center; background: rgba(52, 211, 153, 0.14); border: 1px solid rgba(52, 211, 153, 0.35); color: #34d399; opacity: 0; transform: scale(0.85); transition: opacity 0.2s ease, transform 0.2s ease; }
  .check.ready { opacity: 1; transform: scale(1); }
  .check.error { background: rgba(239, 68, 68, 0.14); border-color: rgba(239, 68, 68, 0.35); color: #f87171; opacity: 1; transform: scale(1); }
  h1 { font-size: 17px; font-weight: 600; letter-spacing: -0.01em; margin: 0 0 6px; }
  p { font-size: 13px; color: #9ca3af; margin: 0; line-height: 1.5; }
</style>
</head>
<body>
  <div class="card">
    <div class="marks">
      <svg width="34" height="34" viewBox="0 0 64 64" fill="none" aria-hidden="true">
        <defs>
          <linearGradient id="gh-p1" x1="18" y1="8" x2="54" y2="32" gradientUnits="userSpaceOnUse"><stop offset="0%" stop-color="#34D399"/><stop offset="100%" stop-color="#0EA5E9"/></linearGradient>
          <linearGradient id="gh-p2" x1="18" y1="8" x2="54" y2="32" gradientUnits="userSpaceOnUse"><stop offset="0%" stop-color="#A78BFA"/><stop offset="100%" stop-color="#38BDF8"/></linearGradient>
          <linearGradient id="gh-p3" x1="18" y1="8" x2="54" y2="32" gradientUnits="userSpaceOnUse"><stop offset="0%" stop-color="#FBBF24"/><stop offset="100%" stop-color="#FB7185"/></linearGradient>
        </defs>
        <g transform="translate(32 32) scale(1.18) translate(-32 -32)">
          <g transform="rotate(0 32 32)"><path fill="url(#gh-p1)" d="M32.5 8.5c12.4 0 21 8.8 21 20.6 0 1.6-1.6 2.6-3 1.8-4.2-2.3-7.4-5.4-9.8-9.3-2.5-4-6-6.2-10.2-6.2-2.9 0-5.4.8-7.4 2.2-1.3.9-2.8-.4-2.1-1.8C23.1 11.4 27.4 8.5 32.5 8.5z"/></g>
          <g transform="rotate(120 32 32)"><path fill="url(#gh-p2)" d="M32.5 8.5c12.4 0 21 8.8 21 20.6 0 1.6-1.6 2.6-3 1.8-4.2-2.3-7.4-5.4-9.8-9.3-2.5-4-6-6.2-10.2-6.2-2.9 0-5.4.8-7.4 2.2-1.3.9-2.8-.4-2.1-1.8C23.1 11.4 27.4 8.5 32.5 8.5z"/></g>
          <g transform="rotate(240 32 32)"><path fill="url(#gh-p3)" d="M32.5 8.5c12.4 0 21 8.8 21 20.6 0 1.6-1.6 2.6-3 1.8-4.2-2.3-7.4-5.4-9.8-9.3-2.5-4-6-6.2-10.2-6.2-2.9 0-5.4.8-7.4 2.2-1.3.9-2.8-.4-2.1-1.8C23.1 11.4 27.4 8.5 32.5 8.5z"/></g>
        </g>
      </svg>
      <span id="badge" class="check" aria-hidden="true">
        <svg id="badge-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
          <polyline points="20 6 9 17 4 12"></polyline>
        </svg>
      </span>
    </div>
    <h1 id="title">Completing sign-in…</h1>
    <p id="msg">Returning to Gram…</p>
  </div>
  <script>
    (async function () {
      const title = document.getElementById('title');
      const msg = document.getElementById('msg');
      const badge = document.getElementById('badge');
      const badgeIcon = document.getElementById('badge-icon');
      const hashParams = new URLSearchParams((window.location.hash || '').replace(/^#/, ''));
      const searchParams = new URLSearchParams(window.location.search || '');
      const token = hashParams.get('auth_token');
      const err = searchParams.get('auth_error') || searchParams.get('error');
      const hid = ${JSON.stringify(hid)};
      if (err || !token) {
        badge.classList.add('error');
        badgeIcon.innerHTML = '<line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line>';
        title.textContent = 'Sign-in could not be completed';
        msg.textContent = err || 'Missing session token from Lyra Hub.';
        return;
      }
      try {
        const res = await fetch('/api/auth/session-token', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ token, hid }),
        });
        if (!res.ok) throw new Error('Session verification failed.');
        badge.classList.add('ready');
        title.textContent = 'Account signed in';
        msg.textContent = 'You can now go back to the Gram desktop app.';
        window.setTimeout(function () {
          try { window.close(); } catch (e) {}
        }, 900);
      } catch (e) {
        badge.classList.add('error');
        badgeIcon.innerHTML = '<line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line>';
        title.textContent = 'Sign-in failed';
        msg.textContent = (e && e.message) ? e.message : 'Unable to verify session token.';
      }
    })();
  </script>
</body>
</html>`;
    return new Response(html, { headers: { 'Content-Type': 'text/html; charset=utf-8' } });
  }

  if (url.pathname === '/api/auth/handoff-status' && req.method === 'GET') {
    pruneDesktopHandoffs();
    const hid = url.searchParams.get('hid') || '';
    const entry = hid ? pendingDesktopHandoffs.get(hid) : undefined;
    if (!entry) {
      return jsonResponse({ status: 'unknown' });
    }
    if (entry.token) {
      const token = entry.token;
      pendingDesktopHandoffs.delete(hid);
      return jsonResponse({ status: 'completed', token });
    }
    return jsonResponse({ status: 'pending' });
  }

  if (url.pathname === '/api/auth/google' && req.method === 'POST') {
    try {
      pruneDesktopHandoffs();
      const body = (await req.json().catch(() => ({}))) as { returnTo?: string; desktop?: boolean };
      const origin =
        req.headers.get('origin') ||
        `${req.headers.get('x-forwarded-proto') || url.protocol.replace(':', '')}://${req.headers.get('host') || url.host}`;
      const isDesktop = Boolean(body.desktop);
      const hid = isDesktop ? crypto.randomUUID() : '';
      if (isDesktop && hid) {
        pendingDesktopHandoffs.set(hid, { createdAt: Date.now() });
      }
      const returnTo = isDesktop
        ? `${origin}/api/auth/handoff?hid=${encodeURIComponent(hid)}`
        : body.returnTo || `${origin}/`;
      const hubUrl = getLyraHubUrl();
      const providers = await fetchLyraAuthProviders();
      if (!providers.googleEnabled) {
        if (hid) pendingDesktopHandoffs.delete(hid);
        return errorResponse('Google sign-in is not configured on Lyra Hub.', 400);
      }
      const startUrl = `${hubUrl}/api/auth/gram-start?redirect=${encodeURIComponent(returnTo)}`;
      if (isDesktop) {
        openInSystemBrowser(startUrl);
        return jsonResponse({ ok: true, desktop: true, handoffId: hid, url: startUrl });
      }
      return jsonResponse({ url: startUrl });
    } catch (err: any) {
      return errorResponse(err?.message || 'Unable to reach Lyra Hub for Google sign-in.', 503);
    }
  }

  if (url.pathname === '/api/auth/session-token' && req.method === 'POST') {
    try {
      const body = (await req.json()) as { token?: string; hid?: string };
      const token = String(body.token || '').trim();
      const hid = String(body.hid || '').trim();
      if (!token) {
        return errorResponse('Missing session token.', 400);
      }
      const authReq = new Request(req.url, {
        headers: { authorization: `Bearer ${token}` },
      });
      const state = await fetchLyraAuthState(authReq);
      if (!state.connected || !state.authenticated || !state.user) {
        return errorResponse('Invalid or expired Lyra session token.', 401);
      }
      if (hid && pendingDesktopHandoffs.has(hid)) {
        pendingDesktopHandoffs.set(hid, { token, createdAt: Date.now() });
        if (process.platform === 'darwin') {
          try {
            Bun.spawn(['osascript', '-e', 'tell application "Gram" to activate'], {
              stdout: 'ignore',
              stderr: 'ignore',
            });
          } catch {}
        }
      }
      const syncResult = await syncWithLyraHub(authReq);
      return new Response(
        JSON.stringify({ ok: true, authenticated: true, user: state.user, synced: syncResult.synced }),
        {
          headers: {
            'Content-Type': 'application/json',
            'Set-Cookie': buildAuthCookie(req, url, 'gram_lyra_token', token),
          },
        }
      );
    } catch (err: any) {
      return errorResponse(err?.message || 'Failed to verify session token.', 500);
    }
  }

  if (url.pathname === '/api/auth/sign-up' && req.method === 'POST') {
    try {
      const body = (await req.json()) as { name?: string; email?: string; password?: string };
      const hubUrl = getLyraHubUrl();
      try {
        const upstream = await fetch(`${hubUrl}/api/auth/sign-up/email`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', Origin: hubUrl },
          body: JSON.stringify({
            name: body.name || body.email?.split('@')[0] || 'User',
            email: body.email || '',
            password: body.password || '',
          }),
          signal: AbortSignal.timeout(10000),
        });
        const data = (await upstream.json().catch(() => ({}))) as any;
        if (!upstream.ok) {
          return errorResponse(data?.message || data?.error || 'Sign up failed.', upstream.status);
        }
        const token = upstream.headers.get('set-auth-token') || data?.token || data?.session?.token;
        const headers = new Headers({ 'Content-Type': 'application/json' });
        if (token) {
          headers.append('Set-Cookie', buildAuthCookie(req, url, 'gram_lyra_token', token));
          void syncWithLyraHub(new Request(req.url, { headers: { authorization: `Bearer ${token}` } }));
        }
        return new Response(
          JSON.stringify({ ok: true, authenticated: true, user: data?.user || null, source: 'lyra' }),
          { headers }
        );
      } catch {
        // Lyra Hub offline: fall back to local SQLite auth.
      }
      const { user, token } = runtime.signUpUser({
        name: body.name,
        email: body.email || '',
        password: body.password || '',
      });
      return new Response(JSON.stringify({ ok: true, authenticated: true, user, source: 'local' }), {
        headers: {
          'Content-Type': 'application/json',
          'Set-Cookie': buildAuthCookie(req, url, 'gram_session', token),
        },
      });
    } catch (err: any) {
      return errorResponse(err?.message || 'Sign up failed.', 400);
    }
  }

  if (url.pathname === '/api/auth/sign-in' && req.method === 'POST') {
    try {
      const body = (await req.json()) as { email?: string; password?: string };
      const hubUrl = getLyraHubUrl();
      try {
        const upstream = await fetch(`${hubUrl}/api/auth/sign-in/email`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', Origin: hubUrl },
          body: JSON.stringify({
            email: body.email || '',
            password: body.password || '',
          }),
          signal: AbortSignal.timeout(10000),
        });
        const data = (await upstream.json().catch(() => ({}))) as any;
        if (!upstream.ok) {
          return errorResponse(data?.message || data?.error || 'Sign in failed.', upstream.status);
        }
        const token = upstream.headers.get('set-auth-token') || data?.token || data?.session?.token;
        const headers = new Headers({ 'Content-Type': 'application/json' });
        if (token) {
          headers.append('Set-Cookie', buildAuthCookie(req, url, 'gram_lyra_token', token));
          await syncWithLyraHub(new Request(req.url, { headers: { authorization: `Bearer ${token}` } }));
        }
        return new Response(
          JSON.stringify({ ok: true, authenticated: true, user: data?.user || null, source: 'lyra' }),
          { headers }
        );
      } catch {
        // Lyra Hub offline: fall back to local SQLite auth.
      }
      const { user, token } = runtime.signInUser({
        email: body.email || '',
        password: body.password || '',
      });
      return new Response(JSON.stringify({ ok: true, authenticated: true, user, source: 'local' }), {
        headers: {
          'Content-Type': 'application/json',
          'Set-Cookie': buildAuthCookie(req, url, 'gram_session', token),
        },
      });
    } catch (err: any) {
      return errorResponse(err?.message || 'Sign in failed.', 401);
    }
  }

  if (url.pathname === '/api/auth/sign-out' && req.method === 'POST') {
    runtime.signOutUserSession(readSessionCookie(req));
    await proxyLyraJson(req, '/api/auth/sign-out', { method: 'POST', body: {} }).catch(() => null);
    const headers = new Headers({ 'Content-Type': 'application/json' });
    headers.append('Set-Cookie', buildAuthCookie(req, url, 'gram_session', '', 0));
    headers.append('Set-Cookie', buildAuthCookie(req, url, 'gram_lyra_token', '', 0));
    return new Response(JSON.stringify({ ok: true, authenticated: false }), { headers });
  }

  return null;
}

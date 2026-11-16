/**
 * Authentication routes: local SQLite session auth and Lyra Hub OAuth/token handoff.
 */

import type { GramAgentRuntime } from '../../runtime/agentRuntime';
import { errorResponse, jsonResponse } from '../http';
import { fetchLyraAuthProviders, fetchLyraAuthState, getLyraHubUrl, proxyLyraJson } from '../lyraClient';

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

  if (url.pathname === '/api/auth/google' && req.method === 'POST') {
    try {
      const body = (await req.json().catch(() => ({}))) as { returnTo?: string };
      const origin =
        req.headers.get('origin') ||
        `${req.headers.get('x-forwarded-proto') || url.protocol.replace(':', '')}://${req.headers.get('host') || url.host}`;
      const returnTo = body.returnTo || `${origin}/`;
      const hubUrl = getLyraHubUrl();
      const handoffUrl = `${hubUrl}/api/auth/gram-handoff?redirect=${encodeURIComponent(returnTo)}`;
      const upstream = await fetch(`${hubUrl}/api/auth/sign-in/social`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Origin: hubUrl },
        body: JSON.stringify({
          provider: 'google',
          callbackURL: handoffUrl,
          disableRedirect: true,
        }),
        signal: AbortSignal.timeout(10000),
      });
      const text = await upstream.text();
      let data: any = {};
      try {
        data = JSON.parse(text);
      } catch {}
      if (!upstream.ok || !data?.url) {
        return errorResponse(
          data?.message || data?.error || 'Google sign-in is not configured on Lyra Hub.',
          upstream.status >= 400 ? upstream.status : 400
        );
      }
      const headers = new Headers({ 'Content-Type': 'application/json' });
      const setCookie = upstream.headers.get('set-cookie');
      if (setCookie) headers.set('set-cookie', setCookie);
      return new Response(JSON.stringify({ url: data.url }), { headers });
    } catch (err: any) {
      return errorResponse(err?.message || 'Unable to reach Lyra Hub for Google sign-in.', 503);
    }
  }

  if (url.pathname === '/api/auth/session-token' && req.method === 'POST') {
    try {
      const body = (await req.json()) as { token?: string };
      const token = String(body.token || '').trim();
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

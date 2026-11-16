/** Interactive headless browser tools (Playwright) for web development inspection and testing. */

import fs from 'node:fs';
import path from 'node:path';
import type { Browser, BrowserContext, Page } from 'playwright';

interface BrowserSession {
  context: BrowserContext;
  page: Page;
  consoleErrors: string[];
  pageErrors: string[];
  lastUsedAt: number;
}

const IDLE_TIMEOUT_MS = 10 * 60_000;
const NAV_TIMEOUT_MS = 20_000;
const ACTION_TIMEOUT_MS = 10_000;

let sharedBrowser: Browser | null = null;
let sharedBrowserLaunch: Promise<Browser> | null = null;
const sessions = new Map<string, BrowserSession>();
let cleanupTimer: ReturnType<typeof setInterval> | null = null;

export function validateBrowserUrl(rawUrl: string): URL {
  let url: URL;
  try {
    url = new URL(rawUrl.trim());
  } catch {
    throw new Error('Please provide a valid http:// or https:// URL.');
  }
  if ((url.protocol !== 'http:' && url.protocol !== 'https:') || url.username || url.password) {
    throw new Error('Only http:// and https:// URLs without embedded credentials are allowed.');
  }
  return url;
}

async function getBrowser(): Promise<Browser> {
  if (sharedBrowser?.isConnected()) return sharedBrowser;
  if (!sharedBrowserLaunch) {
    sharedBrowserLaunch = (async () => {
      const { chromium } = await import('playwright');
      const browser = await chromium.launch({ headless: true });
      sharedBrowser = browser;
      sharedBrowserLaunch = null;
      if (!cleanupTimer) {
        cleanupTimer = setInterval(() => {
          const cutoff = Date.now() - IDLE_TIMEOUT_MS;
          for (const [key, sess] of sessions.entries()) {
            if (sess.lastUsedAt < cutoff) {
              sessions.delete(key);
              sess.context.close().catch(() => {});
            }
          }
          if (sessions.size === 0 && sharedBrowser) {
            const b = sharedBrowser;
            sharedBrowser = null;
            b.close().catch(() => {});
            if (cleanupTimer) {
              clearInterval(cleanupTimer);
              cleanupTimer = null;
            }
          }
        }, 60_000);
        if (typeof cleanupTimer.unref === 'function') cleanupTimer.unref();
      }
      return browser;
    })().catch((err) => {
      sharedBrowserLaunch = null;
      throw err;
    });
  }
  return sharedBrowserLaunch;
}

async function getOrCreateSession(sessionKey: string): Promise<BrowserSession> {
  const existing = sessions.get(sessionKey);
  if (existing && !existing.page.isClosed()) {
    existing.lastUsedAt = Date.now();
    return existing;
  }

  const browser = await getBrowser();
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await context.newPage();
  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];

  page.on('console', (msg) => {
    if (msg.type() === 'error') {
      consoleErrors.push(msg.text().slice(0, 500));
      if (consoleErrors.length > 30) consoleErrors.shift();
    }
  });
  page.on('pageerror', (err) => {
    pageErrors.push((err?.message || String(err)).slice(0, 500));
    if (pageErrors.length > 30) pageErrors.shift();
  });

  const sess: BrowserSession = { context, page, consoleErrors, pageErrors, lastUsedAt: Date.now() };
  sessions.set(sessionKey, sess);
  return sess;
}

function formatRecentErrors(sess: BrowserSession): string {
  const errs = [
    ...sess.pageErrors.slice(-5).map((e) => `[pageerror] ${e}`),
    ...sess.consoleErrors.slice(-5).map((e) => `[console.error] ${e}`),
  ];
  return errs.length ? `\nErrors:\n${errs.join('\n')}` : '';
}

export async function browserNavigate(params: {
  sessionKey: string;
  url: string;
  waitUntil?: 'load' | 'domcontentloaded' | 'networkidle';
}): Promise<string> {
  const url = validateBrowserUrl(params.url);
  const sess = await getOrCreateSession(params.sessionKey);
  sess.consoleErrors.length = 0;
  sess.pageErrors.length = 0;
  const response = await sess.page.goto(url.toString(), {
    waitUntil: params.waitUntil || 'domcontentloaded',
    timeout: NAV_TIMEOUT_MS,
  });
  sess.lastUsedAt = Date.now();
  const title = await sess.page.title().catch(() => '');
  return `Navigated to ${sess.page.url()} (HTTP ${response?.status() ?? 'unknown'})\nTitle: ${title || '(untitled)'}${formatRecentErrors(sess)}`;
}

export async function browserSnapshot(params: {
  sessionKey: string;
  selector?: string;
  maxChars?: number;
}): Promise<string> {
  const sess = sessions.get(params.sessionKey);
  if (!sess || sess.page.isClosed()) {
    throw new Error('No active browser page for this session. Call browser_navigate first.');
  }
  sess.lastUsedAt = Date.now();
  const maxChars = Math.min(14_000, Math.max(500, Number(params.maxChars || 8000)));
  const title = await sess.page.title().catch(() => '');
  const data = await sess.page.evaluate((rootSelector: string | undefined) => {
    const root = (rootSelector ? document.querySelector(rootSelector) : document.body) || document.body;
    const text = (((root as HTMLElement | null)?.innerText) || root?.textContent || '').replace(/\n{3,}/g, '\n\n').trim();
    const interactive: string[] = [];
    const nodes = root?.querySelectorAll('a[href], button, input, textarea, select, [role="button"]') || [];
    for (let i = 0; i < Math.min(nodes.length, 60); i++) {
      const el = nodes[i] as HTMLElement;
      const tag = el.tagName.toLowerCase();
      const id = el.id ? `#${el.id}` : '';
      const name = el.getAttribute('name') ? `[name="${el.getAttribute('name')}"]` : '';
      const aria = el.getAttribute('aria-label') ? `[aria-label="${el.getAttribute('aria-label')}"]` : '';
      const type = el.getAttribute('type') ? `(${el.getAttribute('type')})` : '';
      const label = (el.innerText || el.getAttribute('placeholder') || el.getAttribute('value') || '').replace(/\s+/g, ' ').trim().slice(0, 60);
      interactive.push(`- ${tag}${type}${id}${name}${aria}${label ? `: "${label}"` : ''}`);
    }
    return { text, interactive };
  }, params.selector);

  return [
    `URL: ${sess.page.url()}`,
    `Title: ${title || '(untitled)'}`,
    data.interactive.length ? `Interactive elements (${data.interactive.length}):\n${data.interactive.join('\n')}` : 'Interactive elements: (none)',
    `Visible text:\n${data.text.slice(0, maxChars)}`,
    formatRecentErrors(sess).trim(),
  ].filter(Boolean).join('\n\n');
}

export async function browserClick(params: {
  sessionKey: string;
  selector: string;
}): Promise<string> {
  const sess = sessions.get(params.sessionKey);
  if (!sess || sess.page.isClosed()) {
    throw new Error('No active browser page for this session. Call browser_navigate first.');
  }
  sess.lastUsedAt = Date.now();
  await sess.page.locator(params.selector).first().click({ timeout: ACTION_TIMEOUT_MS });
  await sess.page.waitForLoadState('domcontentloaded', { timeout: 3000 }).catch(() => {});
  const title = await sess.page.title().catch(() => '');
  return `Clicked "${params.selector}" -> ${sess.page.url()} (${title || 'untitled'})${formatRecentErrors(sess)}`;
}

export async function browserType(params: {
  sessionKey: string;
  selector: string;
  text: string;
  clear?: boolean;
  submit?: boolean;
}): Promise<string> {
  const sess = sessions.get(params.sessionKey);
  if (!sess || sess.page.isClosed()) {
    throw new Error('No active browser page for this session. Call browser_navigate first.');
  }
  sess.lastUsedAt = Date.now();
  const locator = sess.page.locator(params.selector).first();
  if (params.clear !== false) {
    await locator.fill(params.text, { timeout: ACTION_TIMEOUT_MS });
  } else {
    await locator.type(params.text, { timeout: ACTION_TIMEOUT_MS });
  }
  if (params.submit) {
    await locator.press('Enter', { timeout: ACTION_TIMEOUT_MS });
    await sess.page.waitForLoadState('domcontentloaded', { timeout: 3000 }).catch(() => {});
  }
  return `Typed into "${params.selector}"${params.submit ? ' and pressed Enter' : ''} -> ${sess.page.url()}${formatRecentErrors(sess)}`;
}

export async function browserScreenshot(params: {
  sessionKey: string;
  workspaceRoot: string;
  fullPage?: boolean;
  filename?: string;
}): Promise<string> {
  const sess = sessions.get(params.sessionKey);
  if (!sess || sess.page.isClosed()) {
    throw new Error('No active browser page for this session. Call browser_navigate first.');
  }
  sess.lastUsedAt = Date.now();
  const dir = path.join(params.workspaceRoot, '.gram', 'screenshots');
  fs.mkdirSync(dir, { recursive: true });
  const safeName = (params.filename || `shot-${Date.now()}.png`).replace(/[^A-Za-z0-9._-]/g, '_');
  const finalName = safeName.endsWith('.png') ? safeName : `${safeName}.png`;
  const targetPath = path.resolve(dir, finalName);
  if (!targetPath.startsWith(dir + path.sep)) {
    throw new Error('Invalid screenshot filename.');
  }
  await sess.page.screenshot({ path: targetPath, fullPage: Boolean(params.fullPage) });
  const relPath = path.relative(params.workspaceRoot, targetPath);
  return `Saved screenshot to ${relPath} (${sess.page.url()})`;
}

export async function browserClose(sessionKey: string): Promise<string> {
  const sess = sessions.get(sessionKey);
  if (!sess) return 'No active browser session to close.';
  sessions.delete(sessionKey);
  await sess.context.close().catch(() => {});
  return 'Closed browser session.';
}

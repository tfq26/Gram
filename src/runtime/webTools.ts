/** Server-side, read-only web search and page retrieval for Gram's agent tools. */

import { lookup } from 'node:dns/promises';
import { isIP } from 'node:net';
import {
  externalServices,
  type WebPageContent,
  type WebPageFetcher,
  type WebSearchProvider,
  type WebSearchResult,
} from '../services/externalContracts';

export type { WebPageContent, WebSearchResult };

const SEARCH_TIMEOUT_MS = 12_000;
const FETCH_TIMEOUT_MS = 15_000;
const MAX_PAGE_BYTES = 1_500_000;
const MAX_PAGE_CHARS = 14_000;
const searchCache = new Map<string, { expiresAt: number; results: WebSearchResult[] }>();

function isPrivateIpv4(address: string): boolean {
  const parts = address.split('.').map(Number);
  if (parts.length !== 4 || parts.some((part) => !Number.isInteger(part) || part < 0 || part > 255)) return true;
  const [a, b] = parts;
  return a === 0 || a === 10 || a === 127 || (a === 169 && b === 254) ||
    (a === 172 && b >= 16 && b <= 31) || (a === 192 && b === 168) ||
    (a === 100 && b >= 64 && b <= 127) || a >= 224;
}

function isPrivateIpv6(address: string): boolean {
  const ip = address.toLowerCase().split('%')[0];
  if (ip === '::' || ip === '::1' || ip.startsWith('fc') || ip.startsWith('fd') || /^fe[89ab]/.test(ip)) return true;
  const mapped = ip.match(/::ffff:(\d+\.\d+\.\d+\.\d+)$/);
  return mapped ? isPrivateIpv4(mapped[1]) : false;
}

async function assertPublicHttpUrl(rawUrl: string): Promise<URL> {
  let url: URL;
  try {
    url = new URL(rawUrl);
  } catch {
    throw new Error('Please provide a valid public HTTP or HTTPS URL.');
  }
  if ((url.protocol !== 'http:' && url.protocol !== 'https:') || url.username || url.password) {
    throw new Error('Only public HTTP and HTTPS URLs are supported.');
  }
  const host = url.hostname.toLowerCase().replace(/^\[|\]$/g, '');
  if (!host || host === 'localhost' || host.endsWith('.localhost') || host.endsWith('.local') || host.endsWith('.internal')) {
    throw new Error('Private and local network URLs are blocked.');
  }
  const ipVersion = isIP(host);
  if (ipVersion === 4 && isPrivateIpv4(host)) throw new Error('Private and local network URLs are blocked.');
  if (ipVersion === 6 && isPrivateIpv6(host)) throw new Error('Private and local network URLs are blocked.');
  if (!ipVersion) {
    const addresses = await lookup(host, { all: true, verbatim: true });
    if (!addresses.length || addresses.some(({ address }) => isIP(address) === 4 ? isPrivateIpv4(address) : isPrivateIpv6(address))) {
      throw new Error('The URL resolves to a private or reserved network address and was blocked.');
    }
  }
  url.hash = '';
  return url;
}

function cleanText(value: string): string {
  return value
    .replace(/<script\b[^>]*>[\s\S]*?<\/script\s*>/gi, ' ')
    .replace(/<style\b[^>]*>[\s\S]*?<\/style\s*>/gi, ' ')
    .replace(/<noscript\b[^>]*>[\s\S]*?<\/noscript\s*>/gi, ' ')
    .replace(/<(?:svg|template|nav|header|footer|aside)\b[^>]*>[\s\S]*?<\/(?:svg|template|nav|header|footer|aside)\s*>/gi, ' ')
    .replace(/<!--[\s\S]*?-->/g, ' ')
    .replace(/<\/(?:p|div|li|h[1-6]|article|section|br|tr)\s*>/gi, '\n')
    .replace(/<[^>]+>/g, ' ')
    .replace(/&nbsp;|&#160;/gi, ' ')
    .replace(/&amp;/gi, '&').replace(/&lt;/gi, '<').replace(/&gt;/gi, '>')
    .replace(/&quot;/gi, '"').replace(/&#39;|&apos;/gi, "'")
    .replace(/&#(\d+);/g, (_match, code: string) => String.fromCodePoint(Number(code)))
    .replace(/&#x([\da-f]+);/gi, (_match, code: string) => String.fromCodePoint(parseInt(code, 16)))
    .replace(/[ \t\f\v]+/g, ' ')
    .replace(/ *\n */g, '\n')
    .replace(/\n{3,}/g, '\n\n')
    .trim();
}

function htmlTitle(html: string): string {
  const match = html.match(/<title\b[^>]*>([\s\S]*?)<\/title\s*>/i);
  return match ? cleanText(match[1]).slice(0, 300) : '';
}

function stripTags(value: string): string {
  return cleanText(value.replace(/<[^>]*>/g, ' '));
}

function decodeCodePoint(match: string, digits: string, radix = 10): string {
  const value = parseInt(digits, radix);
  return Number.isInteger(value) && value >= 0 && value <= 0x10ffff ? String.fromCodePoint(value) : ' ';
}

function safeHttpUrl(value: string): string | null {
  try {
    const url = new URL(value);
    return url.protocol === 'http:' || url.protocol === 'https:' ? url.toString() : null;
  } catch {
    return null;
  }
}

async function searchParallel(query: string, count: number, signal?: AbortSignal): Promise<WebSearchResult[]> {
  const key = process.env.PARALLEL_API_KEY?.trim();
  if (!key) return [];
  const response = await fetch('https://api.parallel.ai/v1/search', {
    method: 'POST',
    headers: { 'x-api-key': key, 'content-type': 'application/json', 'user-agent': 'Gram-Assistant/1.0' },
    body: JSON.stringify({ objective: query, search_queries: [query], mode: 'fast' }),
    signal: AbortSignal.any([AbortSignal.timeout(SEARCH_TIMEOUT_MS), ...(signal ? [signal] : [])]),
  });
  if (!response.ok) throw new Error(`Parallel search returned HTTP ${response.status}.`);
  const body = await response.json() as { results?: Array<{ title?: string; url?: string; excerpts?: string[]; publish_date?: string }> };
  return (body.results || []).flatMap((item) => {
    const url = item.url ? safeHttpUrl(item.url) : null;
    if (!url || !item.title) return [];
    const snippet = [...(item.excerpts || []), ...(item.publish_date ? [`Published: ${item.publish_date}`] : [])].join('\n\n');
    return [{ title: item.title, url, snippet, source: 'Parallel' }];
  }).slice(0, count);
}

async function searchTavily(query: string, count: number, signal?: AbortSignal): Promise<WebSearchResult[]> {
  const key = process.env.TAVILY_API_KEY?.trim();
  if (!key) return [];
  const response = await fetch('https://api.tavily.com/search', {
    method: 'POST',
    headers: { authorization: `Bearer ${key}`, 'content-type': 'application/json', 'user-agent': 'Gram-Assistant/1.0' },
    body: JSON.stringify({ query, topic: /\b(today|yesterday|latest|current|news|recent|this week)\b/i.test(query) ? 'news' : 'general', search_depth: 'basic', max_results: Math.min(15, count), include_answer: false, include_raw_content: false }),
    signal: AbortSignal.any([AbortSignal.timeout(SEARCH_TIMEOUT_MS), ...(signal ? [signal] : [])]),
  });
  if (!response.ok) throw new Error(`Tavily search returned HTTP ${response.status}.`);
  const body = await response.json() as { results?: Array<{ title?: string; url?: string; content?: string }> };
  return (body.results || []).flatMap((item) => {
    const url = item.url ? safeHttpUrl(item.url) : null;
    if (!url || !item.title) return [];
    return [{ title: item.title, url, snippet: item.content || '', source: 'Tavily' }];
  }).slice(0, count);
}

async function searchDuckDuckGo(query: string, count: number, signal?: AbortSignal): Promise<WebSearchResult[]> {
  const url = `https://html.duckduckgo.com/html/?q=${encodeURIComponent(query)}`;
  const response = await fetch(url, {
    headers: { 'user-agent': 'Mozilla/5.0 (compatible; GramAssistant/1.0)', accept: 'text/html' },
    signal: AbortSignal.any([AbortSignal.timeout(SEARCH_TIMEOUT_MS), ...(signal ? [signal] : [])]),
  });
  if (!response.ok) throw new Error(`DuckDuckGo search returned HTTP ${response.status}.`);
  const html = (await response.text()).slice(0, 1_000_000);
  const results: WebSearchResult[] = [];
  const blockRe = /<div\b[^>]*class="[^"]*result[^"]*"[\s\S]*?<\/div>\s*<\/div>/gi;
  for (const block of html.matchAll(blockRe)) {
    const linkMatch = block[0].match(/<a\b[^>]*class="[^"]*result__a[^"]*"[^>]*href="([^"]+)"[^>]*>([\s\S]*?)<\/a>/i);
    if (!linkMatch) continue;
    let href = linkMatch[1].replace(/&amp;/g, '&');
    try {
      const parsed = new URL(href, 'https://duckduckgo.com');
      href = parsed.searchParams.get('uddg') || parsed.toString();
    } catch {}
    const resultUrl = safeHttpUrl(href);
    const title = stripTags(linkMatch[2]);
    const snippetMatch = block[0].match(/class="[^"]*result__snippet[^"]*"[^>]*>([\s\S]*?)<\//i);
    if (resultUrl && title) results.push({ title, url: resultUrl, snippet: snippetMatch ? stripTags(snippetMatch[1]) : '', source: 'DuckDuckGo' });
    if (results.length >= count) break;
  }
  return results;
}

async function searchGoogleNews(query: string, count: number, signal?: AbortSignal): Promise<WebSearchResult[]> {
  const url = `https://news.google.com/rss/search?q=${encodeURIComponent(query)}&hl=en-US&gl=US&ceid=US:en`;
  const response = await fetch(url, {
    headers: { 'user-agent': 'Mozilla/5.0 (compatible; GramAssistant/1.0)', accept: 'application/rss+xml,application/xml,text/xml' },
    signal: AbortSignal.any([AbortSignal.timeout(SEARCH_TIMEOUT_MS), ...(signal ? [signal] : [])]),
  });
  if (!response.ok) throw new Error(`Google News search returned HTTP ${response.status}.`);
  const xml = (await response.text()).slice(0, 1_000_000);
  const results: WebSearchResult[] = [];
  for (const entry of xml.matchAll(/<item\b[^>]*>([\s\S]*?)<\/item>/gi)) {
    const title = entry[1].match(/<title(?:\s[^>]*)?>([\s\S]*?)<\/title>/i)?.[1];
    const link = entry[1].match(/<link(?:\s[^>]*)?>([\s\S]*?)<\/link>/i)?.[1];
    const description = entry[1].match(/<description(?:\s[^>]*)?>([\s\S]*?)<\/description>/i)?.[1];
    const resultUrl = link ? safeHttpUrl(stripTags(link)) : null;
    const cleanTitle = title ? stripTags(title) : '';
    if (resultUrl && cleanTitle) results.push({ title: cleanTitle, url: resultUrl, snippet: description ? stripTags(description) : '', source: 'Google News' });
    if (results.length >= count) break;
  }
  return results;
}

async function searchWikipedia(query: string, count: number, signal?: AbortSignal): Promise<WebSearchResult[]> {
  const url = `https://en.wikipedia.org/w/api.php?action=query&list=search&srsearch=${encodeURIComponent(query)}&format=json&utf8=1&srlimit=${count}`;
  const response = await fetch(url, {
    headers: { 'user-agent': 'GramAssistant/1.0 (web search)', accept: 'application/json' },
    signal: AbortSignal.any([AbortSignal.timeout(SEARCH_TIMEOUT_MS), ...(signal ? [signal] : [])]),
  });
  if (!response.ok) throw new Error(`Wikipedia search returned HTTP ${response.status}.`);
  const body = await response.json() as { query?: { search?: Array<{ title?: string; snippet?: string }> } };
  return (body.query?.search || []).flatMap((item) => {
    if (!item.title) return [];
    const resultUrl = `https://en.wikipedia.org/wiki/${encodeURIComponent(item.title.replace(/ /g, '_'))}`;
    return [{ title: item.title, url: resultUrl, snippet: item.snippet ? stripTags(item.snippet) : '', source: 'Wikipedia' }];
  }).slice(0, count);
}

function dedupe(results: WebSearchResult[], count: number): WebSearchResult[] {
  const seen = new Set<string>();
  return results.filter((item) => {
    const key = item.url.toLowerCase();
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  }).slice(0, count);
}

function rankAndCleanResults(results: WebSearchResult[], query: string, count: number): WebSearchResult[] {
  const terms = query.toLowerCase().match(/[a-z0-9]{3,}/g) || [];
  const unique = dedupe(results, results.length).map((result, index) => {
    const title = cleanText(result.title).slice(0, 300);
    const snippet = cleanText(result.snippet).slice(0, 1800);
    const haystack = `${title} ${snippet}`.toLowerCase();
    const relevance = terms.reduce((score, term) => score + (title.toLowerCase().includes(term) ? 3 : 0) + (snippet.toLowerCase().includes(term) ? 1 : 0), 0);
    let quality = 0;
    try {
      const host = new URL(result.url).hostname.toLowerCase();
      if (host.endsWith('.gov') || host.endsWith('.edu')) quality += 4;
      if (/docs?|developer|api|reference/.test(host)) quality += 2;
      if (/official/.test(`${title} ${snippet}`.toLowerCase())) quality += 1;
    } catch {}
    if (result.source === 'Wikipedia') quality -= 1;
    return { result: { ...result, title, snippet }, score: relevance + quality, index };
  });
  return unique.sort((a, b) => b.score - a.score || a.index - b.index).slice(0, count).map(({ result }) => result);
}

async function searchSearxng(query: string, count: number, signal?: AbortSignal): Promise<WebSearchResult[]> {
  const baseUrl = process.env.SEARXNG_URL?.trim();
  if (!baseUrl) return [];
  const url = `${baseUrl.replace(/\/+$/, '')}/search?q=${encodeURIComponent(query)}&format=json`;
  const response = await fetch(url, {
    headers: { accept: 'application/json', 'user-agent': 'Gram-Assistant/1.0' },
    signal: AbortSignal.any([AbortSignal.timeout(SEARCH_TIMEOUT_MS), ...(signal ? [signal] : [])]),
  });
  if (!response.ok) throw new Error(`SearXNG search returned HTTP ${response.status}.`);
  const body = await response.json() as { results?: Array<{ title?: string; url?: string; content?: string }> };
  return (body.results || []).flatMap((item) => {
    const safeUrl = item.url ? safeHttpUrl(item.url) : null;
    if (!safeUrl || !item.title) return [];
    return [{ title: item.title, url: safeUrl, snippet: item.content || '', source: 'SearXNG' }];
  }).slice(0, count);
}

export class SearxngSearchProvider implements WebSearchProvider {
  public readonly id = 'searxng';
  public readonly label = 'SearXNG';
  public isAvailable(): boolean { return Boolean(process.env.SEARXNG_URL?.trim()); }
  public search(query: string, count: number, signal?: AbortSignal): Promise<WebSearchResult[]> {
    return searchSearxng(query, count, signal);
  }
}

export class ParallelSearchProvider implements WebSearchProvider {
  public readonly id = 'parallel';
  public readonly label = 'Parallel';
  public isAvailable(): boolean { return Boolean(process.env.PARALLEL_API_KEY?.trim()); }
  public search(query: string, count: number, signal?: AbortSignal): Promise<WebSearchResult[]> {
    return searchParallel(query, count, signal);
  }
}

export class TavilySearchProvider implements WebSearchProvider {
  public readonly id = 'tavily';
  public readonly label = 'Tavily';
  public isAvailable(): boolean { return Boolean(process.env.TAVILY_API_KEY?.trim()); }
  public search(query: string, count: number, signal?: AbortSignal): Promise<WebSearchResult[]> {
    return searchTavily(query, count, signal);
  }
}

export class DuckDuckGoSearchProvider implements WebSearchProvider {
  public readonly id = 'duckduckgo';
  public readonly label = 'DuckDuckGo';
  public isAvailable(): boolean { return true; }
  public search(query: string, count: number, signal?: AbortSignal): Promise<WebSearchResult[]> {
    return searchDuckDuckGo(query, count, signal);
  }
}

export class GoogleNewsSearchProvider implements WebSearchProvider {
  public readonly id = 'google-news';
  public readonly label = 'Google News';
  public isAvailable(query = ''): boolean {
    return /\b(today|yesterday|latest|current|news|recent|this week|this month)\b/i.test(query);
  }
  public search(query: string, count: number, signal?: AbortSignal): Promise<WebSearchResult[]> {
    return searchGoogleNews(query, count, signal);
  }
}

export class WikipediaSearchProvider implements WebSearchProvider {
  public readonly id = 'wikipedia';
  public readonly label = 'Wikipedia';
  public isAvailable(): boolean { return true; }
  public search(query: string, count: number, signal?: AbortSignal): Promise<WebSearchResult[]> {
    return searchWikipedia(query, count, signal);
  }
}

export class DefaultWebPageFetcher implements WebPageFetcher {
  public readonly id = 'default-http';
  public fetchPage(url: string, signal?: AbortSignal): Promise<WebPageContent> {
    return fetchWebContentDirect(url, signal);
  }
}

export function registerDefaultWebProviders(): void {
  externalServices.clearWebSearch();
  externalServices.registerWebSearch(new SearxngSearchProvider());
  externalServices.registerWebSearch(new ParallelSearchProvider());
  externalServices.registerWebSearch(new TavilySearchProvider());
  externalServices.registerWebSearch(new DuckDuckGoSearchProvider());
  externalServices.registerWebSearch(new GoogleNewsSearchProvider());
  externalServices.registerWebSearch(new WikipediaSearchProvider());
  externalServices.setWebFetcher(new DefaultWebPageFetcher());
}

registerDefaultWebProviders();

export async function searchWeb(query: string, resultCount = 8, signal?: AbortSignal): Promise<WebSearchResult[]> {
  const cleanQuery = query.trim().slice(0, 500);
  if (!cleanQuery) throw new Error('Search query is required.');
  const count = Math.max(1, Math.min(15, Math.floor(resultCount) || 8));
  const cacheKey = `${cleanQuery.toLowerCase()}::${count}`;
  const cached = searchCache.get(cacheKey);
  if (cached && cached.expiresAt > Date.now()) return cached.results;

  const errors: string[] = [];
  const providers = externalServices.listWebSearch().filter((p) => p.isAvailable(cleanQuery));
  for (const provider of providers) {
    try {
      const results = rankAndCleanResults(await provider.search(cleanQuery, count, signal), cleanQuery, count);
      if (results.length) {
        searchCache.set(cacheKey, { expiresAt: Date.now() + 5 * 60_000, results });
        return results;
      }
    } catch (error) {
      if (signal?.aborted) throw error;
      errors.push(error instanceof Error ? error.message : String(error));
    }
  }
  if (errors.length) throw new Error(`Web search could not reach a search provider. ${errors.join(' ')}`);
  return [];
}

async function readLimited(response: Response): Promise<string> {
  if (!response.body) return '';
  const reader = response.body.getReader();
  const chunks: Uint8Array[] = [];
  let size = 0;
  while (size < MAX_PAGE_BYTES) {
    const { done, value } = await reader.read();
    if (done) break;
    const part = value.subarray(0, MAX_PAGE_BYTES - size);
    chunks.push(part);
    size += part.length;
    if (part.length < value.length) break;
  }
  try { await reader.cancel(); } catch {}
  const bytes = new Uint8Array(size);
  let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
  return new TextDecoder().decode(bytes);
}

async function fetchWebContentDirect(rawUrl: string, signal?: AbortSignal): Promise<WebPageContent> {
  let url = await assertPublicHttpUrl(rawUrl);
  const visited = new Set<string>();
  for (let hop = 0; hop <= 4; hop++) {
    const current = url.toString();
    if (visited.has(current)) throw new Error('The page redirected in a loop.');
    visited.add(current);
    const response = await fetch(current, {
      redirect: 'manual',
      headers: { 'user-agent': 'Mozilla/5.0 (compatible; GramAssistant/1.0)', accept: 'text/html,application/xhtml+xml,text/plain,application/json' },
      signal: AbortSignal.any([AbortSignal.timeout(FETCH_TIMEOUT_MS), ...(signal ? [signal] : [])]),
    });
    if ([301, 302, 303, 307, 308].includes(response.status)) {
      const location = response.headers.get('location');
      if (!location) throw new Error(`The page returned redirect status ${response.status} without a destination.`);
      const next = new URL(location, url);
      if (url.protocol === 'https:' && next.protocol !== 'https:') throw new Error('Redirect from HTTPS to HTTP was blocked.');
      url = await assertPublicHttpUrl(next.toString());
      continue;
    }
    if (!response.ok) throw new Error(`The page returned HTTP ${response.status}.`);
    const contentType = response.headers.get('content-type') || '';
    if (!/(text\/html|application\/xhtml\+xml|text\/plain|application\/json)/i.test(contentType)) {
      throw new Error(`Unsupported page type: ${contentType || 'unknown'}.`);
    }
    const body = await readLimited(response);
    const isHtml = /html|xhtml/i.test(contentType);
    const title = isHtml ? htmlTitle(body) : url.hostname;
    const content = cleanText(isHtml ? body : body).slice(0, MAX_PAGE_CHARS);
    return { title: title || url.hostname, url: current, content };
  }
  throw new Error('The page redirected too many times.');
}

export async function fetchWebContent(rawUrl: string, signal?: AbortSignal): Promise<WebPageContent> {
  const fetcher = externalServices.getWebFetcher();
  if (fetcher) return fetcher.fetchPage(rawUrl, signal);
  return fetchWebContentDirect(rawUrl, signal);
}


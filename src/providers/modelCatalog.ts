/**
 * Model catalog client.
 *
 * Lyra owns the model catalog (ids, prices, capability scores, aliases). Gram keeps a cached copy
 * so routing helpers in `router.ts` stay synchronous and work offline:
 *
 *   built-in (4 models)  ->  disk cache (last good fetch)  ->  live fetch from Lyra
 *
 * Lyra's gateway is the authoritative allowlist. While only the built-in fallback is loaded,
 * `router.isAllowedModel` passes unknown ids through to Lyra instead of rejecting them locally.
 */

import fs from 'node:fs';
import path from 'node:path';
import builtinCatalog from './builtinModels.json';
import type { ModelFamily, ModelProfile, ModelProvider, WorkflowKind } from './router';

/** One model as served by Lyra's `GET /api/gateway/llm/models`. */
export interface LyraCatalogModel {
  id: string;
  name: string;
  family: string;
  tier?: number;
  contextWindow: number;
  supportsPromptCaching: boolean;
  zdrCompliant?: boolean;
  providers?: string[];
  primaryProvider?: string;
  pricing?: { inputPerMillion: number; outputPerMillion: number };
  scores?: { coding: number; reasoning: number; toolProficiency?: number; speed: number };
  workflowGroups?: string[];
  contributorWarning?: string;
  aliases?: string[];
}

export interface LyraCatalogResponse {
  privacyMode?: string;
  catalogVersion?: string;
  models: LyraCatalogModel[];
}

export type CatalogSource = 'builtin' | 'cache' | 'lyra' | 'test';

export interface CatalogState {
  source: CatalogSource;
  version: string;
  fetchedAt: number | null;
  modelCount: number;
}

const DEFAULT_TTL_MS = 6 * 60 * 60 * 1000;

let models = new Map<string, ModelProfile>();
let aliasToId = new Map<string, string>();
let state: CatalogState = { source: 'builtin', version: 'builtin', fetchedAt: null, modelCount: 0 };
let inFlight: Promise<RefreshResult> | null = null;

export type RefreshResult =
  | { ok: true; changed: boolean; version: string }
  | { ok: false; skipped?: boolean; reason: string };

/** Lyra responses from before pricing/scores were added cannot drive local routing. */
function isUsableModel(m: LyraCatalogModel): boolean {
  return Boolean(m?.id && m.pricing && m.scores && typeof m.contextWindow === 'number');
}

export function toModelProfile(m: LyraCatalogModel): ModelProfile {
  const provider = (m.primaryProvider || m.providers?.[0] || 'bedrock') as ModelProvider;
  return {
    id: m.id,
    name: m.name,
    provider,
    family: m.family as ModelFamily,
    workflows: (m.workflowGroups ?? []) as WorkflowKind[],
    inputPricePerM: m.pricing!.inputPerMillion,
    outputPricePerM: m.pricing!.outputPerMillion,
    contextWindow: m.contextWindow,
    supportsPromptCaching: m.supportsPromptCaching,
    contributorWarning: m.contributorWarning,
    capabilities: {
      coding: m.scores!.coding,
      reasoning: m.scores!.reasoning,
      speed: m.scores!.speed,
    },
    aliases: m.aliases ?? [],
  };
}

function applyCatalog(response: LyraCatalogResponse, source: CatalogSource, fetchedAt: number | null): void {
  const nextModels = new Map<string, ModelProfile>();
  const nextAliases = new Map<string, string>();
  for (const raw of response.models) {
    if (!isUsableModel(raw)) continue;
    const profile = toModelProfile(raw);
    nextModels.set(profile.id, profile);
    for (const alias of profile.aliases ?? []) nextAliases.set(alias, profile.id);
  }
  models = nextModels;
  aliasToId = nextAliases;
  state = {
    source,
    version: response.catalogVersion || 'unversioned',
    fetchedAt,
    modelCount: nextModels.size,
  };
}

/** Replace the active catalog. Used by tests and by the fetch/cache paths below. */
export function setModelCatalog(response: LyraCatalogResponse, source: CatalogSource = 'test'): void {
  applyCatalog(response, source, source === 'test' ? null : Date.now());
}

export function resetModelCatalog(): void {
  applyCatalog(builtinCatalog as LyraCatalogResponse, 'builtin', null);
}

export function getCatalogState(): CatalogState {
  return { ...state };
}

export function listCatalogModels(): ModelProfile[] {
  return Array.from(models.values());
}

/** Resolves a canonical id or any legacy alias to its profile. */
export function getCatalogModel(modelId: string): ModelProfile | undefined {
  return models.get(modelId) ?? models.get(aliasToId.get(modelId) ?? '');
}

/**
 * Id to send to a provider when Gram talks to it directly (`GRAM_DIRECT_LLM=true`), where Lyra's
 * canonical ids are not valid. Picks the alias that matches the provider's id style.
 */
export function resolveUpstreamModelId(model: ModelProfile): string {
  const aliases = model.aliases ?? [];
  if (model.provider === 'openrouter') {
    return aliases.find((a) => a.includes('/')) ?? model.id;
  }
  if (model.provider === 'bedrock') {
    const bedrock = aliases.filter((a) => !a.includes('/') && a.includes('.'));
    return bedrock.find((a) => a.startsWith('us.')) ?? bedrock[0] ?? model.id;
  }
  return model.id;
}

// ---------------------------------------------------------------------------------------------
// Disk cache
// ---------------------------------------------------------------------------------------------

export function getCatalogCachePath(): string {
  return process.env.GRAM_MODEL_CATALOG_CACHE || path.join(process.cwd(), '.gram', 'model-catalog.json');
}

interface CatalogCacheFile {
  fetchedAt: number;
  response: LyraCatalogResponse;
}

/** Loads the last good catalog from disk. Returns false (keeping the built-in set) if absent or bad. */
export function loadCachedModelCatalog(cachePath = getCatalogCachePath()): boolean {
  try {
    const parsed = JSON.parse(fs.readFileSync(cachePath, 'utf8')) as CatalogCacheFile;
    if (!parsed?.response || !Array.isArray(parsed.response.models)) return false;
    if (!parsed.response.models.some(isUsableModel)) return false;
    applyCatalog(parsed.response, 'cache', Number(parsed.fetchedAt) || null);
    return true;
  } catch {
    return false;
  }
}

function saveCatalogCache(response: LyraCatalogResponse, cachePath = getCatalogCachePath()): void {
  try {
    fs.mkdirSync(path.dirname(cachePath), { recursive: true });
    const payload: CatalogCacheFile = { fetchedAt: Date.now(), response };
    fs.writeFileSync(cachePath, JSON.stringify(payload), 'utf8');
  } catch {
    // The cache is an optimisation; failing to write it must never break a request.
  }
}

// ---------------------------------------------------------------------------------------------
// Refresh from Lyra
// ---------------------------------------------------------------------------------------------

export interface RefreshOptions {
  /** Auth headers for Lyra (session bearer token or gateway API key). */
  headers?: Record<string, string>;
  /** Refresh even if the cached copy is still fresh. */
  force?: boolean;
  /** Abort the request after this many ms (default 5000). */
  timeoutMs?: number;
  fetchImpl?: typeof fetch;
  cachePath?: string;
}

function ttlMs(): number {
  const raw = Number(process.env.GRAM_MODEL_CATALOG_TTL_MS);
  return Number.isFinite(raw) && raw > 0 ? raw : DEFAULT_TTL_MS;
}

export function isCatalogStale(): boolean {
  if (state.source === 'builtin' || state.source === 'test' || state.fetchedAt === null) {
    return state.source !== 'test';
  }
  return Date.now() - state.fetchedAt > ttlMs();
}

function hubUrl(): string {
  return (process.env.LYRA_HUB_URL || 'https://lyra.2helix.org').replace(/\/+$/, '');
}

function defaultAuthHeaders(): Record<string, string> {
  const apiKey = process.env.LYRA_GATEWAY_API_KEY;
  return apiKey ? { 'x-lyra-api-key': apiKey, Authorization: `Bearer ${apiKey}` } : {};
}

/**
 * Fetches the catalog from Lyra. Never throws: on any failure the current catalog stays active.
 * Always requests the full (`standard`) list; ZDR filtering happens locally in `router.ts`.
 */
export function refreshModelCatalog(options: RefreshOptions = {}): Promise<RefreshResult> {
  if (inFlight) return inFlight;
  inFlight = doRefresh(options).finally(() => {
    inFlight = null;
  });
  return inFlight;
}

/** Refreshes only when the catalog has never been fetched or is older than the TTL. */
export function refreshModelCatalogIfStale(options: RefreshOptions = {}): Promise<RefreshResult> {
  if (!options.force && !isCatalogStale()) {
    return Promise.resolve({ ok: false, skipped: true, reason: 'catalog is fresh' });
  }
  return refreshModelCatalog(options);
}

async function doRefresh(options: RefreshOptions): Promise<RefreshResult> {
  const privacyMode = (process.env.GRAM_PRIVACY_MODE || process.env.LYRA_PRIVACY_MODE || 'standard').toLowerCase();
  if (privacyMode === 'local-only') {
    return { ok: false, skipped: true, reason: 'local-only privacy mode never contacts Lyra' };
  }

  const doFetch = options.fetchImpl ?? fetch;
  const headers: Record<string, string> = {
    Accept: 'application/json',
    ...defaultAuthHeaders(),
    ...(options.headers ?? {}),
  };
  if (state.source === 'lyra' || state.source === 'cache') {
    headers['If-None-Match'] = `"${state.version}"`;
  }

  try {
    const res = await doFetch(`${hubUrl()}/api/gateway/llm/models?privacyMode=standard`, {
      headers,
      signal: AbortSignal.timeout(options.timeoutMs ?? 5000),
    });

    if (res.status === 304) {
      state = { ...state, fetchedAt: Date.now() };
      return { ok: true, changed: false, version: state.version };
    }
    if (!res.ok) return { ok: false, reason: `Lyra returned HTTP ${res.status}` };

    const body = (await res.json()) as LyraCatalogResponse;
    if (!body?.catalogVersion || !Array.isArray(body.models) || !body.models.some(isUsableModel)) {
      return { ok: false, reason: 'Lyra catalog is missing pricing/scores (Lyra not yet updated)' };
    }
    const changed = body.catalogVersion !== state.version;
    applyCatalog(body, 'lyra', Date.now());
    saveCatalogCache(body, options.cachePath);
    return { ok: true, changed, version: body.catalogVersion };
  } catch (err: any) {
    return { ok: false, reason: err?.message || String(err) };
  }
}

resetModelCatalog();

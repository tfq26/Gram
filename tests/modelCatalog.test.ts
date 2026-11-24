import { afterEach, beforeEach, describe, expect, it } from 'bun:test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import fixture from './fixtures/lyra-model-catalog.json';
import {
  getCatalogState,
  isCatalogStale,
  loadCachedModelCatalog,
  refreshModelCatalog,
  refreshModelCatalogIfStale,
  resetModelCatalog,
  resolveUpstreamModelId,
  setModelCatalog,
  type LyraCatalogResponse,
} from '../src/providers/modelCatalog';
import {
  clearUnavailableModels,
  getModelProfile,
  isAllowedModel,
  listAllowedModels,
  markModelUnavailable,
  selectOptimalModel,
} from '../src/providers/router';

const catalog = fixture as LyraCatalogResponse;
const ENV_KEYS = ['LYRA_HUB_URL', 'LYRA_GATEWAY_API_KEY', 'GRAM_PRIVACY_MODE', 'LYRA_PRIVACY_MODE', 'GRAM_MODEL_CATALOG_TTL_MS'];

function jsonResponse(body: unknown, init: ResponseInit = {}): Response {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' }, ...init });
}

interface RecordedCall {
  url: string;
  headers: Record<string, string>;
}

function fakeFetch(handler: (call: RecordedCall) => Response | Promise<Response>) {
  const calls: RecordedCall[] = [];
  const impl = (async (input: any, init?: RequestInit) => {
    const call: RecordedCall = { url: String(input), headers: { ...(init?.headers as Record<string, string>) } };
    calls.push(call);
    return handler(call);
  }) as typeof fetch;
  return { impl, calls };
}

describe('model catalog client', () => {
  const saved: Record<string, string | undefined> = {};
  let tmp = '';
  let cachePath = '';

  beforeEach(() => {
    for (const key of ENV_KEYS) {
      saved[key] = process.env[key];
      delete process.env[key];
    }
    process.env.LYRA_HUB_URL = 'https://lyra.test';
    tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'gram-catalog-'));
    cachePath = path.join(tmp, 'model-catalog.json');
    clearUnavailableModels();
  });

  afterEach(() => {
    fs.rmSync(tmp, { recursive: true, force: true });
    for (const key of ENV_KEYS) {
      if (saved[key] === undefined) delete process.env[key];
      else process.env[key] = saved[key];
    }
    setModelCatalog(catalog, 'test'); // restore the shared test snapshot
  });

  describe('built-in fallback', () => {
    it('serves a small usable catalog before Lyra has ever been reached', () => {
      resetModelCatalog();
      expect(getCatalogState().source).toBe('builtin');
      expect(listAllowedModels().length).toBe(4);
      expect(selectOptimalModel({ taskComplexity: 'simple', priority: 'cost' }).id).toBe('nova-2-lite');
      expect(selectOptimalModel({ taskComplexity: 'architecture', priority: 'frontier' }).id).toBeTruthy();
    });

    it('lets unknown ids through to Lyra instead of rejecting them against an incomplete list', () => {
      resetModelCatalog();
      expect(isAllowedModel('grok-4.20')).toBe(true);
      expect(isAllowedModel('')).toBe(false);
    });

    it('is strict once a real catalog is loaded', () => {
      expect(getCatalogState().source).toBe('test');
      expect(isAllowedModel('not-a-real-model')).toBe(false);
    });
  });

  describe('lookups', () => {
    it('resolves legacy ids and aliases to canonical profiles', () => {
      expect(getModelProfile('us.amazon.nova-2-lite-v1:0')?.id).toBe('nova-2-lite');
      expect(getModelProfile('x-ai/grok-4.20')?.id).toBe('grok-4.20');
      expect(getModelProfile('nova-2-lite')?.id).toBe('nova-2-lite');
      expect(getModelProfile('nope')).toBeUndefined();
    });

    it('resolves provider-style upstream ids for direct mode', () => {
      expect(resolveUpstreamModelId(getModelProfile('nova-2-lite')!)).toBe('us.amazon.nova-2-lite-v1:0');
      expect(resolveUpstreamModelId(getModelProfile('grok-4.20')!)).toBe('x-ai/grok-4.20');
    });

    it('marks a model unavailable whichever id it is referred to by', () => {
      const first = selectOptimalModel({ taskComplexity: 'simple', priority: 'cost' });
      expect(first.id).toBe('nova-2-lite');
      // Mark via a legacy alias; selection must still skip the canonical model.
      markModelUnavailable('us.amazon.nova-2-lite-v1:0');
      expect(selectOptimalModel({ taskComplexity: 'simple', priority: 'cost' }).id).not.toBe('nova-2-lite');
    });
  });

  describe('privacy and selection', () => {
    it('blocks a model with a contributor warning in ZDR mode and excludes it from auto', () => {
      expect(() =>
        selectOptimalModel({ taskComplexity: 'moderate', priority: 'balanced', pinnedModelId: 'muse-spark-1.3', privacyMode: 'zdr' })
      ).toThrow(/ZDR/);
      process.env.OPENROUTER_API_KEY = 'test';
      try {
        const picked = selectOptimalModel({ taskComplexity: 'architecture', priority: 'frontier', privacyMode: 'zdr' });
        expect(picked.contributorWarning).toBeUndefined();
      } finally {
        delete process.env.OPENROUTER_API_KEY;
      }
    });

    it('planning priority only considers models scoring 90+ on coding and reasoning', () => {
      const picked = selectOptimalModel({ taskComplexity: 'complex', priority: 'planning' });
      expect(picked.capabilities.coding).toBeGreaterThanOrEqual(90);
      expect(picked.capabilities.reasoning).toBeGreaterThanOrEqual(90);
    });
  });

  describe('refreshing from Lyra', () => {
    it('fetches, activates and caches the catalog, sending credentials', async () => {
      resetModelCatalog();
      const { impl, calls } = fakeFetch(() => jsonResponse(catalog));

      const result = await refreshModelCatalog({
        fetchImpl: impl,
        cachePath,
        headers: { Authorization: 'Bearer session-token' },
      });

      expect(result).toEqual({ ok: true, changed: true, version: catalog.catalogVersion! });
      expect(calls[0]!.url).toBe('https://lyra.test/api/gateway/llm/models?privacyMode=standard');
      expect(calls[0]!.headers.Authorization).toBe('Bearer session-token');
      expect(calls[0]!.headers['If-None-Match']).toBeUndefined();
      expect(getCatalogState()).toMatchObject({ source: 'lyra', version: catalog.catalogVersion, modelCount: catalog.models.length });
      expect(JSON.parse(fs.readFileSync(cachePath, 'utf8')).response.catalogVersion).toBe(catalog.catalogVersion);
    });

    it('falls back to the gateway API key when no session header is given', async () => {
      process.env.LYRA_GATEWAY_API_KEY = 'gw-key';
      const { impl, calls } = fakeFetch(() => jsonResponse(catalog));
      await refreshModelCatalog({ fetchImpl: impl, cachePath });
      expect(calls[0]!.headers['x-lyra-api-key']).toBe('gw-key');
    });

    it('revalidates with If-None-Match and keeps the catalog on 304', async () => {
      const ok = fakeFetch(() => jsonResponse(catalog));
      await refreshModelCatalog({ fetchImpl: ok.impl, cachePath });

      const notModified = fakeFetch(() => new Response(null, { status: 304 }));
      const result = await refreshModelCatalog({ fetchImpl: notModified.impl, cachePath });

      expect(notModified.calls[0]!.headers['If-None-Match']).toBe(`"${catalog.catalogVersion}"`);
      expect(result).toEqual({ ok: true, changed: false, version: catalog.catalogVersion! });
      expect(listAllowedModels().length).toBe(catalog.models.length);
    });

    it('keeps the current catalog when Lyra errors or is unreachable', async () => {
      resetModelCatalog();
      const http500 = fakeFetch(() => new Response('boom', { status: 500 }));
      expect(await refreshModelCatalog({ fetchImpl: http500.impl, cachePath })).toMatchObject({ ok: false });

      const offline = fakeFetch(() => {
        throw new Error('fetch failed');
      });
      expect(await refreshModelCatalog({ fetchImpl: offline.impl, cachePath })).toMatchObject({ ok: false, reason: 'fetch failed' });

      expect(getCatalogState().source).toBe('builtin');
      expect(listAllowedModels().length).toBe(4);
      expect(fs.existsSync(cachePath)).toBe(false);
    });

    it('rejects a catalog from a Lyra that does not serve pricing and scores yet', async () => {
      resetModelCatalog();
      const legacy = {
        privacyMode: 'standard',
        models: [{ id: 'old-model', name: 'Old', family: 'anthropic', contextWindow: 1000, supportsPromptCaching: false }],
      };
      const { impl } = fakeFetch(() => jsonResponse(legacy));
      const result = await refreshModelCatalog({ fetchImpl: impl, cachePath });
      expect(result).toMatchObject({ ok: false });
      expect(getCatalogState().source).toBe('builtin');
    });

    it('never contacts Lyra in local-only privacy mode', async () => {
      process.env.GRAM_PRIVACY_MODE = 'local-only';
      const { impl, calls } = fakeFetch(() => jsonResponse(catalog));
      const result = await refreshModelCatalog({ fetchImpl: impl, cachePath });
      expect(result).toMatchObject({ ok: false, skipped: true });
      expect(calls.length).toBe(0);
    });

    it('shares one request between concurrent refreshes', async () => {
      resetModelCatalog();
      const { impl, calls } = fakeFetch(() => jsonResponse(catalog));
      await Promise.all([refreshModelCatalog({ fetchImpl: impl, cachePath }), refreshModelCatalog({ fetchImpl: impl, cachePath })]);
      expect(calls.length).toBe(1);
    });
  });

  describe('staleness and the disk cache', () => {
    it('refreshes only when the catalog is stale', async () => {
      const { impl, calls } = fakeFetch(() => jsonResponse(catalog));
      resetModelCatalog();
      expect(isCatalogStale()).toBe(true);

      await refreshModelCatalogIfStale({ fetchImpl: impl, cachePath });
      expect(isCatalogStale()).toBe(false);

      const skipped = await refreshModelCatalogIfStale({ fetchImpl: impl, cachePath });
      expect(skipped).toMatchObject({ ok: false, skipped: true });
      expect(calls.length).toBe(1);

      process.env.GRAM_MODEL_CATALOG_TTL_MS = '1';
      await Bun.sleep(5);
      expect(isCatalogStale()).toBe(true);
    });

    it('restores the last good catalog from disk', async () => {
      const { impl } = fakeFetch(() => jsonResponse(catalog));
      await refreshModelCatalog({ fetchImpl: impl, cachePath });

      resetModelCatalog();
      expect(listAllowedModels().length).toBe(4);
      expect(loadCachedModelCatalog(cachePath)).toBe(true);
      expect(getCatalogState().source).toBe('cache');
      expect(listAllowedModels().length).toBe(catalog.models.length);
    });

    it('ignores a missing or corrupt cache and keeps the built-in set', () => {
      resetModelCatalog();
      expect(loadCachedModelCatalog(path.join(tmp, 'absent.json'))).toBe(false);
      fs.writeFileSync(cachePath, '{not json');
      expect(loadCachedModelCatalog(cachePath)).toBe(false);
      fs.writeFileSync(cachePath, JSON.stringify({ fetchedAt: 1, response: { models: [] } }));
      expect(loadCachedModelCatalog(cachePath)).toBe(false);
      expect(getCatalogState().source).toBe('builtin');
    });
  });
});

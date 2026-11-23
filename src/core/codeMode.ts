/**
 * Gram Code Mode — Isolated Programmatic Agent Execution Engine
 *
 * Replaces 10-15 slow, multi-turn JSON tool roundtrips with a single
 * typed TypeScript/JavaScript script executed inside an isolated Bun Worker
 * against the host-bridged `gram.*` RPC SDK.
 */

import type { Database } from 'bun:sqlite';
import type { GramFS } from './gramfs';
import type { SemanticGraph, BlastRadiusResult } from './semanticGraph';

export interface GramSdkContext {
  fs: {
    read: (path: string, layerId?: string) => string;
    write: (path: string, content: string, layerId?: string) => string;
    exists: (path: string, layerId?: string) => boolean;
    delete: (path: string, layerId?: string) => string;
    listModified: (layerId?: string) => Array<{ path: string; isDeleted: boolean }>;
  };
  symbol: {
    get: (uri: string) => any;
    replace: (filePath: string, uri: string, newCode: string, layerId?: string) => boolean;
    blastRadius: (uri: string) => BlastRadiusResult;
    list: () => any[];
  };
  probe: {
    evalFn: <T = unknown>(fnCode: string, inputArgs: unknown[]) => Promise<T>;
  };
  verify: {
    syntaxCheck: (code: string) => { valid: boolean; error?: string };
  };
  memory: {
    pinFact: (key: string, fact: string) => void;
    getFact: (key: string) => string | undefined;
    listFacts: () => Record<string, string>;
  };
  db: {
    listTables: () => string[];
    queryReadOnly: <T = Record<string, unknown>>(sql: string, params?: unknown[], maxRows?: number) => T[];
  };
}

export interface CodeModeExecutionResult {
  success: boolean;
  returnValue: unknown;
  logs: string[];
  durationMs: number;
  error?: string;
}

export interface GramCodeModeOptions {
  db?: Database;
}

interface GramSdkDocEntry {
  namespace: string;
  summary: string;
  declaration: string;
}

const GRAM_SDK_DOCS: GramSdkDocEntry[] = [
  {
    namespace: 'gram.fs',
    summary: 'Copy-on-write GramFS overlay filesystem operations.',
    declaration: `declare namespace gram.fs {
  function read(path: string, layerId?: string): Promise<string>;
  function write(path: string, content: string, layerId?: string): Promise<string>;
  function exists(path: string, layerId?: string): Promise<boolean>;
  function delete(path: string, layerId?: string): Promise<string>;
  function listModified(layerId?: string): Promise<Array<{ path: string; isDeleted: boolean }>>;
}`,
  },
  {
    namespace: 'gram.symbol',
    summary: 'SemanticGraph symbol lookup, blast-radius simulation, and AST replacement.',
    declaration: `declare namespace gram.symbol {
  function get(uri: string): Promise<unknown>;
  function replace(filePath: string, uri: string, newCode: string, layerId?: string): Promise<boolean>;
  function blastRadius(uri: string): Promise<unknown>;
  function list(): Promise<unknown[]>;
}`,
  },
  {
    namespace: 'gram.verify',
    summary: 'In-memory syntax validation.',
    declaration: `declare namespace gram.verify {
  function syntaxCheck(code: string): Promise<{ valid: boolean; error?: string }>;
}`,
  },
  {
    namespace: 'gram.memory',
    summary: 'Session/SQLite-backed key-value fact pinning.',
    declaration: `declare namespace gram.memory {
  function pinFact(key: string, fact: string): Promise<void>;
  function getFact(key: string): Promise<string | undefined>;
  function listFacts(): Promise<Record<string, string>>;
}`,
  },
  {
    namespace: 'gram.db',
    summary: 'Read-only SQLite inspection of Gram workspace & session tables.',
    declaration: `declare namespace gram.db {
  function listTables(): Promise<string[]>;
  function queryReadOnly<T = Record<string, unknown>>(sql: string, params?: unknown[], maxRows?: number): Promise<T[]>;
}`,
  },
];

const GRAM_WORKER_BOOTSTRAP = `
"use strict";
const _post = self.postMessage.bind(self);
let _seq = 0;
const _pending = new Map();

self.addEventListener("message", async (ev) => {
  const msg = ev.data;
  if (!msg || typeof msg !== "object") return;

  if (msg.type === "rpc_res") {
    const entry = _pending.get(msg.id);
    if (!entry) return;
    _pending.delete(msg.id);
    if (msg.error) entry.reject(new Error(msg.error));
    else entry.resolve(msg.result);
    return;
  }

  if (msg.type === "exec") {
    const logs = [];
    const customConsole = {
      log: (...args) => logs.push(args.map((a) => typeof a === "string" ? a : JSON.stringify(a)).join(" ")),
      warn: (...args) => logs.push("[WARN] " + args.map(String).join(" ")),
      error: (...args) => logs.push("[ERROR] " + args.map(String).join(" ")),
    };

    const callRpc = (ns, method, args) => new Promise((resolve, reject) => {
      const id = ++_seq;
      _pending.set(id, { resolve, reject });
      _post({ type: "rpc_req", id, ns, method, args });
    });

    const makeNamespace = (ns) => new Proxy({}, {
      get(_t, prop) {
        if (typeof prop !== "string") return undefined;
        return (...args) => callRpc(ns, prop, args);
      }
    });

    const gram = new Proxy({}, {
      get(_t, ns) {
        if (typeof ns !== "string") return undefined;
        return makeNamespace(ns);
      }
    });

    try {
      const AsyncFunction = Object.getPrototypeOf(async function(){}).constructor;
      const fn = new AsyncFunction(
        "gram",
        "console",
        "Bun",
        "process",
        "fetch",
        "WebSocket",
        "XMLHttpRequest",
        "importScripts",
        "localStorage",
        "sessionStorage",
        "indexedDB",
        msg.script
      );
      const result = await fn(gram, customConsole);
      _post({ type: "exec_done", success: true, returnValue: result ?? null, logs });
    } catch (err) {
      _post({ type: "exec_done", success: false, returnValue: null, logs, error: err && err.message ? err.message : String(err) });
    }
  }
});
`;

export class GramCodeMode {
  private pinnedFacts = new Map<string, string>();
  private readonly db?: Database;

  constructor(
    private gramfs: GramFS,
    private graph: SemanticGraph,
    options?: GramCodeModeOptions
  ) {
    this.db = options?.db;
    if (this.db) {
      this.db.exec(`CREATE TABLE IF NOT EXISTS gram_codemode_memory (
        key TEXT PRIMARY KEY,
        fact TEXT NOT NULL,
        updated_at TEXT NOT NULL
      )`);
    }
  }

  /**
   * Code Mode discovery primitive: returns TypeScript SDK declarations for `gram.*`.
   */
  public search(query?: string): { namespaces: string[]; sdkDeclarations: string } {
    const q = (query || '').trim().toLowerCase();
    const matched = q
      ? GRAM_SDK_DOCS.filter(
          (d) =>
            d.namespace.toLowerCase().includes(q) ||
            d.summary.toLowerCase().includes(q) ||
            d.declaration.toLowerCase().includes(q)
        )
      : GRAM_SDK_DOCS;

    const list = matched.length > 0 ? matched : GRAM_SDK_DOCS;
    return {
      namespaces: list.map((d) => d.namespace),
      sdkDeclarations: list.map((d) => `// ${d.summary}\n${d.declaration}`).join('\n\n'),
    };
  }

  /**
   * Builds the scoped `gram.*` SDK instance for host-side RPC dispatch.
   */
  public buildSdk(layerId = 'main', logs: string[] = []): GramSdkContext {
    return {
      fs: {
        read: (relPath: string, layer = layerId) => this.gramfs.readFile(relPath, layer),
        write: (relPath: string, content: string, layer = layerId) =>
          this.gramfs.writeFile(relPath, content, layer),
        exists: (relPath: string, layer = layerId) => this.gramfs.fileExists(relPath, layer),
        delete: (relPath: string, layer = layerId) => this.gramfs.deleteFile(relPath, layer),
        listModified: (layer = layerId) => this.gramfs.listModifiedFiles(layer),
      },
      symbol: {
        get: (uri: string) => this.graph.getNode(uri),
        replace: (filePath: string, uri: string, newCode: string, layer = layerId) => {
          const current = this.gramfs.readFile(filePath, layer);
          const updated = this.graph.replaceSymbol(current, uri, newCode);
          this.gramfs.writeFile(filePath, updated, layer);
          this.graph.indexFile(filePath, updated);
          return true;
        },
        blastRadius: (uri: string) => this.graph.blastRadiusSimulate(uri),
        list: () => this.graph.listSymbols(),
      },
      probe: {
        evalFn: async <T>(fnCode: string, inputArgs: unknown[]): Promise<T> => {
          const wrapped = new Function('args', `
            const fn = (${fnCode});
            return fn(...args);
          `);
          return wrapped(inputArgs);
        },
      },
      verify: {
        syntaxCheck: (code: string) => {
          try {
            new Function(code);
            return { valid: true };
          } catch (err: any) {
            return { valid: false, error: err.message };
          }
        },
      },
      memory: {
        pinFact: (key: string, fact: string) => {
          this.pinnedFacts.set(key, fact);
          if (this.db) {
            this.db
              .query(
                `INSERT INTO gram_codemode_memory (key, fact, updated_at) VALUES (?, ?, ?)
                 ON CONFLICT(key) DO UPDATE SET fact = excluded.fact, updated_at = excluded.updated_at`
              )
              .run(key, fact, new Date().toISOString());
          }
          logs.push(`[Memory Pin] ${key}: ${fact}`);
        },
        getFact: (key: string) => {
          if (this.db) {
            const row = this.db
              .query(`SELECT fact FROM gram_codemode_memory WHERE key = ? LIMIT 1`)
              .get(key) as { fact: string } | null;
            if (row) return row.fact;
          }
          return this.pinnedFacts.get(key);
        },
        listFacts: () => {
          if (this.db) {
            const rows = this.db
              .query(`SELECT key, fact FROM gram_codemode_memory ORDER BY key`)
              .all() as Array<{ key: string; fact: string }>;
            const out: Record<string, string> = {};
            for (const r of rows) out[r.key] = r.fact;
            return out;
          }
          return Object.fromEntries(this.pinnedFacts.entries());
        },
      },
      db: {
        listTables: () => {
          if (!this.db) return [];
          const rows = this.db
            .query(`SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name`)
            .all() as Array<{ name: string }>;
          return rows.map((r) => r.name);
        },
        queryReadOnly: <T = Record<string, unknown>>(
          sql: string,
          params: unknown[] = [],
          maxRows = 100
        ): T[] => {
          if (!this.db) throw new Error('GramCodeMode: no SQLite database attached.');
          const trimmed = sql.trim().replace(/;+\s*$/, '');
          if (!/^(SELECT|WITH|PRAGMA\s+table_info)\b/i.test(trimmed)) {
            throw new Error('Only read-only SELECT / WITH queries are permitted.');
          }
          if (/;\s*\S/.test(trimmed)) {
            throw new Error('Multiple SQL statements are not permitted.');
          }
          const capped = Math.min(Math.max(1, maxRows), 500);
          const hasLimit = /\bLIMIT\s+\d+\b/i.test(trimmed);
          const finalSql =
            hasLimit || /^PRAGMA\b/i.test(trimmed) ? trimmed : `${trimmed} LIMIT ${capped}`;
          return this.db.query(finalSql).all(...(params as any[])) as T[];
        },
      },
    };
  }

  /**
   * Executes a programmatic TypeScript/JavaScript script inside an isolated Bun Worker
   * with the `gram.*` RPC SDK injected and ambient host globals scrubbed.
   */
  public async execute(
    script: string,
    layerId = 'main',
    timeoutMs = 5000
  ): Promise<CodeModeExecutionResult> {
    const logs: string[] = [];
    const sdk = this.buildSdk(layerId, logs);
    const startTime = performance.now();
    const boundedTimeout = Math.max(100, Math.min(timeoutMs, 30_000));

    const blob = new Blob([GRAM_WORKER_BOOTSTRAP], { type: 'application/javascript' });
    const workerUrl = URL.createObjectURL(blob);
    const worker = new Worker(workerUrl);

    try {
      return await new Promise<CodeModeExecutionResult>((resolve) => {
        let settled = false;
        const finish = (res: CodeModeExecutionResult) => {
          if (settled) return;
          settled = true;
          clearTimeout(timer);
          resolve(res);
        };

        const timer = setTimeout(() => {
          finish({
            success: false,
            returnValue: null,
            logs,
            durationMs: Math.round(performance.now() - startTime),
            error: `Code Mode script timed out after ${boundedTimeout}ms`,
          });
        }, boundedTimeout);

        worker.addEventListener('message', async (ev: MessageEvent) => {
          const msg = ev.data as any;
          if (!msg || typeof msg !== 'object') return;

          if (msg.type === 'rpc_req') {
            try {
              const namespaceObj = (sdk as any)[msg.ns];
              if (!namespaceObj || typeof namespaceObj[msg.method] !== 'function') {
                throw new Error(`Unknown gram.${msg.ns}.${msg.method}`);
              }
              const result = await namespaceObj[msg.method](...(msg.args || []));
              worker.postMessage({ type: 'rpc_res', id: msg.id, result });
            } catch (err: any) {
              worker.postMessage({
                type: 'rpc_res',
                id: msg.id,
                error: err?.message || String(err),
              });
            }
            return;
          }

          if (msg.type === 'exec_done') {
            const workerLogs = Array.isArray(msg.logs) ? msg.logs : [];
            finish({
              success: Boolean(msg.success),
              returnValue: msg.returnValue ?? null,
              logs: [...logs, ...workerLogs],
              durationMs: Math.round(performance.now() - startTime),
              error: msg.error,
            });
          }
        });

        worker.addEventListener('error', (err) => {
          finish({
            success: false,
            returnValue: null,
            logs,
            durationMs: Math.round(performance.now() - startTime),
            error: err?.message || 'Worker execution error',
          });
        });

        worker.postMessage({ type: 'exec', script });
      });
    } finally {
      worker.terminate();
      URL.revokeObjectURL(workerUrl);
    }
  }
}

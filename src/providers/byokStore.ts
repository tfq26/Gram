import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import type { Database } from 'bun:sqlite';
import type { ModelProfile } from './router';

const KEYCHAIN_SERVICE = 'com.gram.byok';
const KEYCHAIN_ACCOUNT = 'master-key';
const ENC_PREFIX = 'enc:v1:';
let cachedMasterKey: Buffer | null = null;

function getMasterKey(): Buffer {
  if (cachedMasterKey) return cachedMasterKey;

  const envHex = process.env.GRAM_BYOK_MASTER_KEY?.trim();
  if (envHex && /^[0-9a-f]{64}$/i.test(envHex)) {
    cachedMasterKey = Buffer.from(envHex, 'hex');
    return cachedMasterKey;
  }

  if (process.platform === 'darwin') {
    try {
      const hex = execFileSync('security', ['find-generic-password', '-s', KEYCHAIN_SERVICE, '-a', KEYCHAIN_ACCOUNT, '-w'], {
        encoding: 'utf8',
        stdio: ['ignore', 'pipe', 'ignore'],
      }).trim();
      if (/^[0-9a-f]{64}$/i.test(hex)) {
        cachedMasterKey = Buffer.from(hex, 'hex');
        return cachedMasterKey;
      }
    } catch {}
  }

  const candidateFiles = [
    path.join(os.homedir(), '.gram', '.byok-key'),
    path.join(process.cwd(), '.gram', '.byok-key'),
  ];
  for (const keyFile of candidateFiles) {
    try {
      if (fs.existsSync(keyFile)) {
        const hex = fs.readFileSync(keyFile, 'utf8').trim();
        if (/^[0-9a-f]{64}$/i.test(hex)) {
          cachedMasterKey = Buffer.from(hex, 'hex');
          return cachedMasterKey;
        }
      }
    } catch {}
  }

  const fresh = crypto.randomBytes(32);
  const freshHex = fresh.toString('hex');
  let storedInKeychain = false;
  if (process.platform === 'darwin') {
    try {
      execFileSync('security', ['add-generic-password', '-U', '-s', KEYCHAIN_SERVICE, '-a', KEYCHAIN_ACCOUNT, '-w', freshHex], {
        stdio: 'ignore',
      });
      storedInKeychain = true;
    } catch {}
  }
  if (!storedInKeychain) {
    for (const keyFile of candidateFiles) {
      try {
        fs.mkdirSync(path.dirname(keyFile), { recursive: true, mode: 0o700 });
        fs.writeFileSync(keyFile, freshHex, { mode: 0o600 });
        break;
      } catch {}
    }
  }
  cachedMasterKey = fresh;
  return fresh;
}

export function encryptByokSecret(plaintext: string): string {
  if (!plaintext) return plaintext;
  if (plaintext.startsWith(ENC_PREFIX)) return plaintext;
  const key = getMasterKey();
  const iv = crypto.randomBytes(12);
  const cipher = crypto.createCipheriv('aes-256-gcm', key, iv);
  const encrypted = Buffer.concat([cipher.update(plaintext, 'utf8'), cipher.final()]);
  const tag = cipher.getAuthTag();
  return `${ENC_PREFIX}${iv.toString('base64')}:${tag.toString('base64')}:${encrypted.toString('base64')}`;
}

export function decryptByokSecret(stored: string): string {
  if (!stored || !stored.startsWith(ENC_PREFIX)) return stored;
  const parts = stored.slice(ENC_PREFIX.length).split(':');
  if (parts.length !== 3) return '';
  const [ivB64, tagB64, dataB64] = parts;
  const key = getMasterKey();
  const decipher = crypto.createDecipheriv('aes-256-gcm', key, Buffer.from(ivB64!, 'base64'));
  decipher.setAuthTag(Buffer.from(tagB64!, 'base64'));
  return Buffer.concat([decipher.update(Buffer.from(dataB64!, 'base64')), decipher.final()]).toString('utf8');
}

export type ByokProviderType = 'openai' | 'anthropic' | 'openrouter' | 'ollama' | 'custom';

export interface ByokModelRecord {
  id: string;
  name: string;
  provider: ByokProviderType;
  modelId: string;
  baseUrl?: string;
  apiKey?: string;
  createdAt: string;
  updatedAt: string;
}

export interface PublicByokModel {
  id: string;
  name: string;
  provider: ByokProviderType;
  modelId: string;
  baseUrl?: string;
  hasApiKey: boolean;
  source: 'byok';
  createdAt: string;
  updatedAt: string;
}

function defaultBaseUrlForProvider(provider: ByokProviderType): string {
  switch (provider) {
    case 'openai':
      return 'https://api.openai.com/v1';
    case 'openrouter':
      return 'https://openrouter.ai/api/v1';
    case 'ollama':
      return 'http://127.0.0.1:11434/v1';
    case 'anthropic':
      return 'https://api.anthropic.com/v1';
    case 'custom':
    default:
      return '';
  }
}

export class ByokModelStore {
  constructor(private db: Database) {
    this.initSchema();
  }

  private initSchema(): void {
    this.db.run(`
      CREATE TABLE IF NOT EXISTS byok_models (
        id TEXT PRIMARY KEY,
        name TEXT NOT NULL,
        provider TEXT NOT NULL,
        model_id TEXT NOT NULL,
        base_url TEXT,
        api_key TEXT,
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL
      );
    `);
  }

  public listPublicModels(): PublicByokModel[] {
    const rows = this.db
      .query<any, []>(`SELECT * FROM byok_models ORDER BY updated_at DESC`)
      .all();
    return rows.map((row) => ({
      id: String(row.id),
      name: String(row.name),
      provider: (row.provider || 'custom') as ByokProviderType,
      modelId: String(row.model_id),
      baseUrl: row.base_url ? String(row.base_url) : undefined,
      hasApiKey: Boolean(row.api_key && String(row.api_key).trim()),
      source: 'byok',
      createdAt: String(row.created_at),
      updatedAt: String(row.updated_at),
    }));
  }

  public getModel(id: string): ByokModelRecord | null {
    const row = this.db
      .query<any, [string]>(`SELECT * FROM byok_models WHERE id = ? LIMIT 1`)
      .get(id);
    if (!row) return null;
    return {
      id: String(row.id),
      name: String(row.name),
      provider: (row.provider || 'custom') as ByokProviderType,
      modelId: String(row.model_id),
      baseUrl: row.base_url ? String(row.base_url) : undefined,
      apiKey: row.api_key ? decryptByokSecret(String(row.api_key)) : undefined,
      createdAt: String(row.created_at),
      updatedAt: String(row.updated_at),
    };
  }

  public upsertModel(input: {
    id?: string;
    name: string;
    provider: ByokProviderType;
    modelId: string;
    baseUrl?: string;
    apiKey?: string;
  }): PublicByokModel {
    const now = new Date().toISOString();
    const cleanModelId = input.modelId.trim();
    const id = input.id?.trim() || `byok:${cleanModelId.replace(/[^a-zA-Z0-9._-]+/g, '-')}-${crypto.randomUUID().slice(0, 6)}`;
    const existing = this.getModel(id);
    const baseUrl = input.baseUrl?.trim() || defaultBaseUrlForProvider(input.provider);
    const rawApiKey = input.apiKey !== undefined ? input.apiKey.trim() : existing?.apiKey;
    const encryptedApiKey = rawApiKey ? encryptByokSecret(rawApiKey) : null;

    this.db.run(
      `INSERT INTO byok_models (id, name, provider, model_id, base_url, api_key, created_at, updated_at)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?)
       ON CONFLICT(id) DO UPDATE SET
         name = excluded.name,
         provider = excluded.provider,
         model_id = excluded.model_id,
         base_url = excluded.base_url,
         api_key = COALESCE(NULLIF(excluded.api_key, ''), byok_models.api_key),
         updated_at = excluded.updated_at`,
      [
        id,
        input.name.trim() || cleanModelId,
        input.provider,
        cleanModelId,
        baseUrl || null,
        encryptedApiKey,
        existing?.createdAt || now,
        now,
      ]
    );

    return this.listPublicModels().find((m) => m.id === id)!;
  }

  public deleteModel(id: string): boolean {
    const res = this.db.run(`DELETE FROM byok_models WHERE id = ?`, [id]);
    return res.changes > 0;
  }

  public toModelProfile(record: ByokModelRecord): ModelProfile & {
    byokConfig: { provider: ByokProviderType; modelId: string; baseUrl?: string; apiKey?: string };
  } {
    return {
      id: record.id,
      name: record.name,
      provider: 'openrouter',
      family: record.provider === 'anthropic' ? 'anthropic' : 'openai',
      contextWindow: 128_000,
      inputPricePerM: 0,
      outputPricePerM: 0,
      supportsPromptCaching: false,
      capabilities: { coding: 9.5, reasoning: 9.4, speed: 8.5 },
      byokConfig: {
        provider: record.provider,
        modelId: record.modelId,
        baseUrl: record.baseUrl || defaultBaseUrlForProvider(record.provider),
        apiKey: record.apiKey,
      },
    };
  }
}

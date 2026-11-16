import { Database } from 'bun:sqlite';
import { describe, expect, test } from 'bun:test';
import { isSecretFilePath, redactSecretText, redactSecretsInValue } from './secretGuard';
import { selectOptimalModel } from './router';
import { ByokModelStore, decryptByokSecret, encryptByokSecret } from './byokStore';
import { compactLoopMessages } from '../runtime/agentRuntime';
import type { LlmMessage } from './llmClient';

describe('Gram Secret Guard & ZDR Routing', () => {
  test('blocks sensitive file paths and directories', () => {
    expect(isSecretFilePath('.env')).toBe(true);
    expect(isSecretFilePath('apps/web/.env.production')).toBe(true);
    expect(isSecretFilePath('.ssh/id_ed25519')).toBe(true);
    expect(isSecretFilePath('certs/server.pem')).toBe(true);
    expect(isSecretFilePath('config/secrets.json')).toBe(true);
    expect(isSecretFilePath('src/runtime/agentTools.ts')).toBe(false);
  });

  test('redacts inline secrets and nested objects', () => {
    const sample = [
      'AWS=AKIA1234567890ABCDEF',
      'OPENAI=sk-proj-abcdefghijklmnopqrstuvwxyz123456',
      'Authorization: Bearer super_secret_token_999',
      '-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA\n-----END RSA PRIVATE KEY-----',
    ].join('\n');

    const redacted = redactSecretText(sample);
    expect(redacted).not.toContain('AKIA1234567890ABCDEF');
    expect(redacted).not.toContain('sk-proj-abcdefghijklmnopqrstuvwxyz123456');
    expect(redacted).not.toContain('super_secret_token_999');
    expect(redacted).not.toContain('MIIEowIBAAKCAQEA');
    expect(redacted).toContain('[REDACTED_AWS_KEY]');
    expect(redacted).toContain('[REDACTED_TOKEN]');
    expect(redacted).toContain('[REDACTED_PRIVATE_KEY]');

    const obj = redactSecretsInValue({ apiKey: 'secret-123', notes: 'Bearer mytoken123' });
    expect(obj.apiKey).toBe('[REDACTED]');
    expect(obj.notes).toBe('Bearer [REDACTED]');
  });

  test('blocks contributor-training models in zdr mode', () => {
    expect(() =>
      selectOptimalModel({
        taskComplexity: 'moderate',
        priority: 'balanced',
        pinnedModelId: 'meta/muse-spark-1.3',
        privacyMode: 'zdr',
      })
    ).toThrow(/ZDR privacy mode/);

    const safe = selectOptimalModel({
      taskComplexity: 'moderate',
      priority: 'balanced',
      privacyMode: 'zdr',
    });
    expect(safe.contributorWarning).toBeUndefined();
  });

  test('encrypts BYOK API keys at rest in SQLite and decrypts on read', () => {
    const cipher = encryptByokSecret('sk-test-byok-secret-999');
    expect(cipher.startsWith('enc:v1:')).toBe(true);
    expect(cipher).not.toContain('sk-test-byok-secret-999');
    expect(decryptByokSecret(cipher)).toBe('sk-test-byok-secret-999');

    const db = new Database(':memory:');
    const store = new ByokModelStore(db);
    const pub = store.upsertModel({
      name: 'Local Test',
      provider: 'openai',
      modelId: 'gpt-4o-mini',
      apiKey: 'sk-live-secret-abc',
    });
    const rawRow = db.query<any, [string]>(`SELECT api_key FROM byok_models WHERE id = ?`).get(pub.id);
    expect(String(rawRow.api_key).startsWith('enc:v1:')).toBe(true);
    expect(store.getModel(pub.id)?.apiKey).toBe('sk-live-secret-abc');
  });

  test('compacts older tool outputs in long agent loops', () => {
    const longOutput = 'x'.repeat(2000);
    const msgs: LlmMessage[] = [
      { role: 'user', content: 'Initial goal' },
      ...Array.from({ length: 10 }, (_, idx) => ({
        role: (idx % 2 === 0 ? 'assistant' : 'user') as 'assistant' | 'user',
        content: idx % 2 === 0
          ? [{ type: 'tool_use' as const, id: `t${idx}`, name: 'read_file', input: { path: 'a.ts' } }]
          : [{ type: 'tool_result' as const, toolUseId: `t${idx - 1}`, content: longOutput }],
      })),
    ];
    const compacted = compactLoopMessages(msgs, 5000);
    expect(compacted.length).toBeLessThanOrEqual(msgs.length);
    expect(JSON.stringify(compacted).length).toBeLessThan(JSON.stringify(msgs).length);
  });
});

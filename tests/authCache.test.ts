import { describe, expect, test } from 'bun:test';
import { Database } from 'bun:sqlite';
import { initRuntimeSchema } from '../src/db/schema';
import { AuthStore } from '../src/db/authStore';

describe('AuthStore offline cached hub sessions', () => {
  test('caches, retrieves, and invalidates hub sessions by token hash', () => {
    const db = new Database(':memory:');
    initRuntimeSchema(db);
    const store = new AuthStore(db);

    const token = 'lyra-session-secret-token-123';
    const user = { id: 'usr-1', name: 'Taufeeq', email: 'taufeeq@example.com' };

    expect(store.getCachedHubSession(token)).toBeNull();

    store.cacheHubSession(token, user);
    const cached = store.getCachedHubSession(token);
    expect(cached).not.toBeNull();
    expect(cached?.user).toEqual(user);

    // Raw token must not be stored in plaintext
    const rows = db.query<{ token_hash: string }, []>('SELECT token_hash FROM cached_hub_sessions').all();
    expect(rows).toHaveLength(1);
    expect(rows[0].token_hash).not.toBe(token);
    expect(rows[0].token_hash).toHaveLength(64);

    // Invalidating removes the cached session
    store.invalidateCachedHubSession(token);
    expect(store.getCachedHubSession(token)).toBeNull();
  });

  test('ignores expired cached hub sessions', () => {
    const db = new Database(':memory:');
    initRuntimeSchema(db);
    const store = new AuthStore(db);

    const token = 'expired-token';
    store.cacheHubSession(token, { id: 'usr-2', name: 'Test', email: 'test@example.com' }, -1000);
    expect(store.getCachedHubSession(token)).toBeNull();
  });
});

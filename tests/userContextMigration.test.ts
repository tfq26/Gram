import { describe, expect, test } from 'bun:test';
import { Database } from 'bun:sqlite';
import { UserContextStore } from '../src/db/userContextStore';

describe('UserContextStore legacy schema migration', () => {
  test('migrates legacy context_key UNIQUE table to UNIQUE (category, context_key) and saves via rememberFromConversation', () => {
    const db = new Database(':memory:');
    db.run(`
      CREATE TABLE user_context_items (
        id TEXT PRIMARY KEY,
        category TEXT NOT NULL,
        context_key TEXT NOT NULL UNIQUE,
        value TEXT NOT NULL,
        source TEXT NOT NULL DEFAULT 'explicit',
        confidence REAL NOT NULL DEFAULT 1,
        source_session_id TEXT,
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL,
        last_confirmed_at TEXT NOT NULL,
        evidence_text TEXT
      );
    `);
    db.run(
      `INSERT INTO user_context_items (id, category, context_key, value, source, confidence, created_at, updated_at, last_confirmed_at)
       VALUES ('uctx-legacy', 'other', 'legacy.key', 'Existing fact', 'manual', 1, '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.000Z')`
    );

    const store = new UserContextStore(db);
    const saved = store.rememberFromConversation({
      category: 'relationships',
      key: 'girlfriend',
      value: 'Loves their girlfriend',
      evidence: 'I love my girlfriend!!',
      sessionId: 'session-test',
    });

    expect(saved.category).toBe('relationships');
    expect(saved.key).toBe('girlfriend');
    expect(store.getProfile().items.length).toBe(2);
  });
});

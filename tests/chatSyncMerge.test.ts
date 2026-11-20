import { describe, expect, test } from 'bun:test';
import { Database } from 'bun:sqlite';
import { ChatStore } from '../src/db/chatStore';
import { initRuntimeSchema } from '../src/db/schema';

describe('ChatStore canonical semantic sync merge', () => {
  test('does not fork conflict when remote JSONB reorders keys or adds metadata with identical text', () => {
    const db = new Database(':memory:');
    initRuntimeSchema(db);
    const store = new ChatStore(db);

    store.saveChatSession({
      sessionId: 's-1',
      title: 'Test',
      projectId: 'local',
      projectLabel: 'Local',
      modelId: 'auto',
      messages: [
        {
          id: 'm-1',
          role: 'assistant',
          content: 'Same exact answer.',
          steps: [{ id: 'st-1', command: 'List files', status: 'done' }],
        },
      ],
    });

    store.applyRemoteSyncDelta({
      latestSeq: 2,
      chats: [
        {
          sessionId: 's-1',
          title: 'Test',
          projectId: 'local',
          projectLabel: 'Local',
          modelId: 'auto',
          revision: 2,
          updatedAt: new Date(Date.now() + 1000).toISOString(),
          messages: [
            {
              // Reordered keys + extra metadata, identical text
              content: 'Same exact answer.',
              role: 'assistant',
              id: 'm-1',
              runId: 'run-99',
              steps: [
                { status: 'done', command: 'List files', id: 'st-1' },
                { status: 'done', command: 'Read file', id: 'st-2' },
              ],
            },
          ],
        },
      ],
    });

    const loaded = store.getChatSession('s-1');
    expect(loaded?.messages.length).toBe(1);
    expect((loaded?.messages[0] as any).syncConflictOf).toBeUndefined();
    expect((loaded?.messages[0] as any).steps.length).toBe(2);
  });

  test('preserves genuine text divergence as a sync conflict', () => {
    const db = new Database(':memory:');
    initRuntimeSchema(db);
    const store = new ChatStore(db);

    store.saveChatSession({
      sessionId: 's-2',
      title: 'Divergent',
      projectId: 'local',
      projectLabel: 'Local',
      modelId: 'auto',
      messages: [{ id: 'm-1', role: 'assistant', content: 'Local version text.' }],
    });

    store.applyRemoteSyncDelta({
      latestSeq: 3,
      chats: [
        {
          sessionId: 's-2',
          title: 'Divergent',
          projectId: 'local',
          projectLabel: 'Local',
          modelId: 'auto',
          revision: 3,
          updatedAt: new Date(Date.now() + 1000).toISOString(),
          messages: [{ id: 'm-1', role: 'assistant', content: 'Remote version text.' }],
        },
      ],
    });

    const loaded = store.getChatSession('s-2');
    expect(loaded?.messages.length).toBe(2);
    expect((loaded?.messages[1] as any).syncConflictOf).toBe('m-1');
  });
});

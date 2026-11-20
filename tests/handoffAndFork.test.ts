import { describe, expect, test } from 'bun:test';
import { Database } from 'bun:sqlite';
import { initRuntimeSchema } from '../src/db/schema';
import { ChatStore } from '../src/db/chatStore';
import { ContextEnvelope } from '../src/core/contextEnvelope';
import { selectOptimalModel } from '../src/providers/router';

describe('Chat Forking & Model Handoff', () => {
  test('ChatStore.forkChatSession slices messages and records fork lineage', () => {
    const db = new Database(':memory:');
    initRuntimeSchema(db);
    const store = new ChatStore(db);

    store.saveChatSession({
      sessionId: 'sess-root',
      title: 'Root Thread',
      projectId: 'proj-1',
      projectLabel: 'Gram',
      modelId: 'us.xai.grok-4.7',
      mode: 'code',
      messages: [
        { id: 'm1', role: 'user', content: 'Research social trends' },
        { id: 'm2', role: 'assistant', content: 'Here is what is trending...' },
        { id: 'm3', role: 'user', content: 'Wait, pivot to something else' },
      ],
    });

    const forked = store.forkChatSession({
      sourceSessionId: 'sess-root',
      messageId: 'm2',
      targetModelId: 'us.anthropic.claude-sonnet-5-5',
      title: 'Implementation Fork',
      baseCheckpointRunId: 'run-42',
    });

    expect(forked).not.toBeNull();
    expect(forked?.parentSessionId).toBe('sess-root');
    expect(forked?.forkedFromMessageId).toBe('m2');
    expect(forked?.forkedAtSequence).toBe(2);
    expect(forked?.baseCheckpointRunId).toBe('run-42');
    expect(forked?.modelId).toBe('us.anthropic.claude-sonnet-5-5');
    expect(forked?.messages).toHaveLength(2);
    expect((forked?.messages[1] as any).id).toBe('m2');

    const listed = store.listChatSessions(10, 'code');
    const listedFork = listed.find((s) => s.sessionId === forked?.sessionId);
    expect(listedFork?.parentSessionId).toBe('sess-root');
  });

  test('ContextEnvelope.buildHandoffCapsule transfers pinnedFacts and plan across families', () => {
    const envelope = new ContextEnvelope({
      sessionId: 'sess-1',
      workspacePath: '/tmp/workspace',
      activeModel: 'us.xai.grok-4.7',
      activeFamily: 'xai' as any,
    });
    envelope.pinFact('arch', 'Use Oberon for sync');
    envelope.updatePlan([{ id: 'p1', title: 'Add fork API', status: 'in_progress', dependencies: [] }]);
    envelope.appendTurn({ role: 'user', content: 'Let us design the fork API' });
    envelope.appendTurn({ role: 'assistant', content: 'We can add POST /api/chat/fork', thinking: 'internal thoughts' });

    const capsule = envelope.buildHandoffCapsule('us.anthropic.claude-sonnet-5-5', 'workflow_shift_to_coding');
    expect(capsule.fromModelId).toBe('us.xai.grok-4.7');
    expect(capsule.toModelId).toBe('us.anthropic.claude-sonnet-5-5');
    expect(capsule.toFamily).toBe('anthropic');
    expect(capsule.pinnedFacts.arch).toBe('Use Oberon for sync');
    expect(capsule.activePlan).toHaveLength(1);
    expect(capsule.recentTurns).toHaveLength(2);
    expect((capsule.recentTurns[1] as any).thinking).toBeUndefined();
    expect(envelope.state.activeModel).toBe('us.anthropic.claude-sonnet-5-5');
    expect(envelope.state.activeFamily).toBe('anthropic');
  });

  test('selectOptimalModel filters by workflow group', () => {
    const socialModel = selectOptimalModel({
      taskComplexity: 'moderate',
      priority: 'balanced',
      workflow: 'world-social',
    });
    expect(socialModel.family).toBe('xai');
    expect(socialModel.workflows).toContain('world-social');

    const codingModel = selectOptimalModel({
      taskComplexity: 'complex',
      priority: 'balanced',
      workflow: 'coding',
    });
    expect(codingModel.workflows).toContain('coding');
  });
});

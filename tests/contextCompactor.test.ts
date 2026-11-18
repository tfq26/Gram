import { describe, expect, test } from 'bun:test';
import {
  compactContextWithSummary,
  estimateContextTokens,
  prepareInitialMessages,
  pruneStaleToolOutputs,
} from '../src/core/contextCompactor';
import type { LlmMessage } from '../src/providers/llmClient';

describe('contextCompactor', () => {
  test('Tier 1 pruneStaleToolOutputs deduplicates superseded file reads and keeps errors', () => {
    const msgs: LlmMessage[] = [
      { role: 'user', content: 'Inspect app.ts' },
      {
        role: 'assistant',
        content: [{ type: 'tool_use', id: 't1', name: 'read_file', input: { path: 'src/app.ts' } }],
      },
      {
        role: 'user',
        content: [{ type: 'tool_result', toolUseId: 't1', content: 'A'.repeat(1200) }],
      },
      {
        role: 'assistant',
        content: [{ type: 'tool_use', id: 't2', name: 'run_command', input: { command: 'bun test' } }],
      },
      {
        role: 'user',
        content: [{ type: 'tool_result', toolUseId: 't2', content: 'Error: failed on line 10', isError: true }],
      },
      {
        role: 'assistant',
        content: [{ type: 'tool_use', id: 't3', name: 'read_file', input: { path: 'src/app.ts' } }],
      },
      {
        role: 'user',
        content: [{ type: 'tool_result', toolUseId: 't3', content: 'UPDATED_CONTENT_V2' }],
      },
    ];

    const res = pruneStaleToolOutputs(msgs, { tailKeepCount: 2 });
    expect(res.prunedCount).toBe(1);
    const firstResult = (res.messages[2]!.content as any[])[0];
    expect(firstResult.content).toContain('Superseded by newer read_file:src/app.ts');
    const errorResult = (res.messages[4]!.content as any[])[0];
    expect(errorResult.content).toBe('Error: failed on line 10');
    const latestRead = (res.messages[6]!.content as any[])[0];
    expect(latestRead.content).toBe('UPDATED_CONTENT_V2');
  });

  test('estimateContextTokens calibrates against lastKnownInputTokens + delta messages', () => {
    const messages: LlmMessage[] = [
      { role: 'user', content: 'Hello' },
      { role: 'assistant', content: 'Hi' },
      { role: 'user', content: 'X'.repeat(340) },
    ];
    const calibrated = estimateContextTokens({
      systemPrompt: 'sys',
      tools: [],
      messages,
      lastKnownInputTokens: 5000,
      lastKnownMessageCount: 2,
    });
    expect(calibrated).toBeGreaterThan(5090);
    expect(calibrated).toBeLessThan(5150);
  });

  test('Tier 2 compactContextWithSummary summarizes middle turns and preserves alternation', async () => {
    const messages: LlmMessage[] = [
      { role: 'user', content: 'Build feature X' },
      { role: 'assistant', content: 'R'.repeat(18_000) },
      { role: 'user', content: 'Step 2 ' + 'U'.repeat(18_000) },
      { role: 'assistant', content: 'R2'.repeat(9_000) },
      { role: 'user', content: 'Step 3' },
      { role: 'assistant', content: 'Working on step 3 now.' },
    ];

    const res = await compactContextWithSummary({
      systemPrompt: 'System instructions',
      tools: [],
      messages,
      contextWindow: 24_000,
      outputReserveTokens: 4_000,
      summarizeFn: async () => '- Goal: Build feature X\n- Status: Moving to Step 3',
    });

    expect(res.compacted).toBe(true);
    expect(res.strategy).toBe('tier2-llm');
    expect(res.afterTokens).toBeLessThan(res.beforeTokens);
    expect(res.messages[0]!.content).toBe('Build feature X');
    expect(JSON.stringify(res.messages)).toContain('COMPACTED_CONTEXT_SUMMARY');
    for (let i = 1; i < res.messages.length; i++) {
      expect(res.messages[i]!.role).not.toBe(res.messages[i - 1]!.role);
    }
  });

  test('prepareInitialMessages keeps >16 turns when inside contextWindow budget', async () => {
    const history = Array.from({ length: 24 }, (_, idx) => ({
      role: (idx % 2 === 0 ? 'user' : 'assistant') as 'user' | 'assistant',
      content: `Turn ${idx}`,
    }));

    const res = await prepareInitialMessages({
      history,
      prompt: 'Latest question',
      systemPrompt: 'sys',
      tools: [],
      contextWindow: 200_000,
    });

    expect(res.compactedHistoryTurns).toBe(0);
    expect(res.messages.length).toBe(25);
  });
});

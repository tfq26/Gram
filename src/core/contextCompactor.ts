/**
 * Two-Tier Long-Horizon Context Compaction & Token Accounting
 *
 * Tier 1 (Zero-cost, deterministic):
 * - Deduplicates superseded read_file / list_files tool outputs
 * - Stubs stale non-error tool outputs outside the recent tail window
 *
 * Tier 2 (Hysteresis high-water/low-water compaction):
 * - Triggers only when estimated context crosses 75% of effective contextWindow,
 *   compacting down to ~38% to preserve prompt-cache prefixes across turns.
 * - Uses an injected cheap LLM summarizer (e.g. Nova 2 Lite) when available,
 *   with a deterministic structured fallback for offline/local-only modes.
 */

import type { LlmContentBlock, LlmMessage, LlmToolDef } from '../providers/llmClient';

export const COMPACTION_HIGH_WATER_RATIO = 0.75;
export const COMPACTION_LOW_WATER_RATIO = 0.38;
export const DEFAULT_OUTPUT_RESERVE_TOKENS = 8_192;

export function estimateTextTokens(text: string): number {
  if (!text) return 0;
  return Math.ceil(text.length / 3.4);
}

export function estimateMessageTokens(msg: LlmMessage): number {
  let tokens = 8;
  if (typeof msg.content === 'string') {
    return tokens + estimateTextTokens(msg.content);
  }
  if (Array.isArray(msg.content)) {
    for (const block of msg.content) {
      if (block.type === 'text') {
        tokens += estimateTextTokens(block.text);
      } else if (block.type === 'tool_result') {
        tokens += estimateTextTokens(block.content) + 12;
      } else if (block.type === 'tool_use') {
        tokens += estimateTextTokens(block.name) + estimateTextTokens(JSON.stringify(block.input || {})) + 16;
      }
    }
  }
  return tokens;
}

export function estimatePromptOverheadTokens(systemPrompt: string, tools: LlmToolDef[]): number {
  const sysTokens = estimateTextTokens(systemPrompt);
  const toolsTokens = tools.length > 0 ? estimateTextTokens(JSON.stringify(tools)) : 0;
  return sysTokens + toolsTokens;
}

export function estimateMessagesTokens(messages: LlmMessage[]): number {
  let total = 0;
  for (const m of messages) {
    total += estimateMessageTokens(m);
  }
  return total;
}

export function estimateContextTokens(params: {
  systemPrompt: string;
  tools: LlmToolDef[];
  messages: LlmMessage[];
  lastKnownInputTokens?: number;
  lastKnownMessageCount?: number;
}): number {
  const { systemPrompt, tools, messages, lastKnownInputTokens, lastKnownMessageCount } = params;
  if (
    typeof lastKnownInputTokens === 'number' &&
    lastKnownInputTokens > 0 &&
    typeof lastKnownMessageCount === 'number' &&
    lastKnownMessageCount >= 0 &&
    messages.length >= lastKnownMessageCount
  ) {
    const deltaMessages = messages.slice(lastKnownMessageCount);
    return lastKnownInputTokens + estimateMessagesTokens(deltaMessages);
  }
  return estimatePromptOverheadTokens(systemPrompt, tools) + estimateMessagesTokens(messages);
}

function isToolResultOnlyUserMessage(msg: LlmMessage): boolean {
  if (msg.role !== 'user' || typeof msg.content === 'string') return false;
  return msg.content.length > 0 && msg.content.every((b) => b.type === 'tool_result');
}

function getToolTargetKey(name: string, input: Record<string, unknown>): string | null {
  if (name === 'read_file' || name === 'list_files') {
    const target = String(input.path || input.directory || '.').trim();
    return `${name}:${target}`;
  }
  return null;
}

/**
 * Tier 1 ($0): Deduplicate superseded read_file/list_files outputs and compact
 * verbose non-error tool outputs outside the recent tail window.
 */
export function pruneStaleToolOutputs(
  messages: LlmMessage[],
  options?: { tailKeepCount?: number; maxStaleToolChars?: number }
): { messages: LlmMessage[]; prunedCount: number } {
  const tailKeepCount = options?.tailKeepCount ?? 4;
  const maxStaleToolChars = options?.maxStaleToolChars ?? 360;
  if (messages.length <= tailKeepCount) {
    return { messages, prunedCount: 0 };
  }

  const tailStart = messages.length - tailKeepCount;
  const toolUseMeta = new Map<string, { name: string; targetKey: string | null; msgIdx: number }>();
  const latestTargetIdx = new Map<string, number>();

  for (let i = 0; i < messages.length; i++) {
    const msg = messages[i]!;
    if (msg.role !== 'assistant' || !Array.isArray(msg.content)) continue;
    for (const block of msg.content) {
      if (block.type !== 'tool_use') continue;
      const targetKey = getToolTargetKey(block.name, block.input || {});
      toolUseMeta.set(block.id, { name: block.name, targetKey, msgIdx: i });
      if (targetKey) {
        latestTargetIdx.set(targetKey, i);
      }
    }
  }

  let prunedCount = 0;
  const nextMessages = messages.map((msg, idx) => {
    if (idx >= tailStart || typeof msg.content === 'string' || !Array.isArray(msg.content)) {
      return msg;
    }

    let msgChanged = false;
    const nextBlocks: LlmContentBlock[] = msg.content.map((block) => {
      if (block.type !== 'tool_result' || block.isError) return block;

      const meta = toolUseMeta.get(block.toolUseId);
      if (meta?.targetKey) {
        const latestIdx = latestTargetIdx.get(meta.targetKey);
        if (latestIdx !== undefined && latestIdx > meta.msgIdx) {
          const stub = `[Superseded by newer ${meta.targetKey} at turn ${latestIdx}]`;
          if (block.content !== stub) {
            msgChanged = true;
            prunedCount += 1;
            return { ...block, content: stub };
          }
          return block;
        }
      }

      if (block.content.length > maxStaleToolChars) {
        msgChanged = true;
        prunedCount += 1;
        return {
          ...block,
          content: `${block.content.slice(0, 220)}\n... [compacted ${block.content.length} chars]`,
        };
      }
      return block;
    });

    return msgChanged ? { ...msg, content: nextBlocks } : msg;
  });

  return { messages: nextMessages, prunedCount };
}

function formatMessagesForSummary(messages: LlmMessage[]): string {
  const lines: string[] = [];
  for (const msg of messages) {
    if (typeof msg.content === 'string') {
      lines.push(`[${msg.role}]: ${msg.content.slice(0, 1200)}`);
      continue;
    }
    const parts: string[] = [];
    for (const block of msg.content) {
      if (block.type === 'text') {
        parts.push(block.text.slice(0, 800));
      } else if (block.type === 'tool_use') {
        parts.push(`call ${block.name}(${JSON.stringify(block.input || {}).slice(0, 200)})`);
      } else if (block.type === 'tool_result') {
        parts.push(`result(${block.isError ? 'error' : 'ok'}): ${block.content.slice(0, 300)}`);
      }
    }
    if (parts.length > 0) {
      lines.push(`[${msg.role}]: ${parts.join(' | ')}`);
    }
  }
  return lines.join('\n').slice(0, 24_000);
}

export function buildDeterministicSummary(messages: LlmMessage[]): string {
  const userPrompts: string[] = [];
  const actions: string[] = [];
  const assistantNotes: string[] = [];

  for (const msg of messages) {
    if (typeof msg.content === 'string') {
      if (msg.role === 'user') userPrompts.push(msg.content.trim().slice(0, 240));
      else assistantNotes.push(msg.content.trim().slice(0, 280));
      continue;
    }
    for (const block of msg.content) {
      if (block.type === 'text' && block.text.trim()) {
        if (msg.role === 'user') userPrompts.push(block.text.trim().slice(0, 240));
        else assistantNotes.push(block.text.trim().slice(0, 280));
      } else if (block.type === 'tool_use') {
        const target = String(block.input?.path || block.input?.command || block.input?.query || '').slice(0, 100);
        actions.push(`${block.name}${target ? `(${target})` : ''}`);
      }
    }
  }

  return [
    '## Compacted Context Summary',
    userPrompts.length ? `**Earlier Requests:**\n- ${userPrompts.slice(-4).join('\n- ')}` : '',
    actions.length ? `**Earlier Tool Actions (${actions.length}):** ${actions.slice(-12).join(', ')}` : '',
    assistantNotes.length ? `**Key Findings / Outputs:**\n- ${assistantNotes.slice(-3).join('\n- ')}` : '',
  ]
    .filter(Boolean)
    .join('\n\n');
}

export interface CompactContextResult {
  messages: LlmMessage[];
  compacted: boolean;
  strategy?: 'tier1-prune' | 'tier2-llm' | 'tier2-deterministic';
  beforeTokens: number;
  afterTokens: number;
  summarizedTurns: number;
  summaryText?: string;
}

/**
 * Tier 2: Hysteresis compaction when context exceeds high-water threshold (75%).
 * Preserves the initial goal message and a clean recent tail, summarizing the middle.
 */
export async function compactContextWithSummary(params: {
  systemPrompt: string;
  tools: LlmToolDef[];
  messages: LlmMessage[];
  contextWindow: number;
  outputReserveTokens?: number;
  lastKnownInputTokens?: number;
  lastKnownMessageCount?: number;
  summarizeFn?: (transcript: string) => Promise<string | null>;
}): Promise<CompactContextResult> {
  const { systemPrompt, tools, messages, contextWindow, summarizeFn } = params;
  const reserve = params.outputReserveTokens ?? DEFAULT_OUTPUT_RESERVE_TOKENS;
  const effectiveBudget = Math.max(8_000, contextWindow - reserve);
  const highWaterTokens = Math.floor(effectiveBudget * COMPACTION_HIGH_WATER_RATIO);
  const lowWaterTokens = Math.floor(effectiveBudget * COMPACTION_LOW_WATER_RATIO);

  const beforeTokens = estimateContextTokens({
    systemPrompt,
    tools,
    messages,
    lastKnownInputTokens: params.lastKnownInputTokens,
    lastKnownMessageCount: params.lastKnownMessageCount,
  });

  if (beforeTokens < highWaterTokens || messages.length <= 4) {
    return {
      messages,
      compacted: false,
      beforeTokens,
      afterTokens: beforeTokens,
      summarizedTurns: 0,
    };
  }

  // Keep the first user message pinned if it's a plain text user prompt
  const hasPinnedHead = messages[0]?.role === 'user' && typeof messages[0].content === 'string';
  const startIdx = hasPinnedHead ? 1 : 0;

  // Find clean tail boundary (never split tool_use -> tool_result pairs)
  const overheadTokens = estimatePromptOverheadTokens(systemPrompt, tools);
  const headTokens = hasPinnedHead ? estimateMessageTokens(messages[0]!) : 0;
  const targetTailBudget = Math.max(2_000, lowWaterTokens - overheadTokens - headTokens - 700);

  let tailStartIdx = messages.length;
  let tailTokens = 0;
  for (let i = messages.length - 1; i >= startIdx + 2; i--) {
    const msg = messages[i]!;
    const msgTokens = estimateMessageTokens(msg);
    if (tailTokens + msgTokens > targetTailBudget && messages.length - i >= 2) {
      break;
    }
    tailTokens += msgTokens;
    tailStartIdx = i;
  }

  // Advance tailStartIdx if it landed on a tool_result-only user message so tool_use isn't orphaned
  while (tailStartIdx < messages.length - 1 && isToolResultOnlyUserMessage(messages[tailStartIdx]!)) {
    tailStartIdx += 1;
  }
  // Ensure tail starts with a user message when head+summary ends with assistant, or keep clean alternation
  if (tailStartIdx <= startIdx) {
    return {
      messages,
      compacted: false,
      beforeTokens,
      afterTokens: beforeTokens,
      summarizedTurns: 0,
    };
  }

  const head = hasPinnedHead ? [messages[0]!] : [];
  const evicted = messages.slice(startIdx, tailStartIdx);
  const tail = messages.slice(tailStartIdx);

  if (evicted.length === 0) {
    return {
      messages,
      compacted: false,
      beforeTokens,
      afterTokens: beforeTokens,
      summarizedTurns: 0,
    };
  }

  let summaryText = '';
  let strategy: 'tier2-llm' | 'tier2-deterministic' = 'tier2-deterministic';
  if (summarizeFn) {
    try {
      const transcript = formatMessagesForSummary(evicted);
      const llmSummary = await summarizeFn(transcript);
      if (llmSummary && llmSummary.trim().length > 20) {
        summaryText = llmSummary.trim();
        strategy = 'tier2-llm';
      }
    } catch {}
  }
  if (!summaryText) {
    summaryText = buildDeterministicSummary(evicted);
  }

  const summaryBlock = `<COMPACTED_CONTEXT_SUMMARY>\n${summaryText}\n</COMPACTED_CONTEXT_SUMMARY>`;
  const rebuilt: LlmMessage[] = [...head];

  if (rebuilt.length === 0) {
    rebuilt.push({ role: 'user', content: summaryBlock });
  } else if (rebuilt[rebuilt.length - 1]!.role === 'user') {
    rebuilt.push({ role: 'assistant', content: summaryBlock });
  } else {
    rebuilt.push({ role: 'user', content: summaryBlock });
  }

  // Guarantee user/assistant alternation going into tail
  if (tail.length > 0 && tail[0]!.role === rebuilt[rebuilt.length - 1]!.role) {
    rebuilt.push({
      role: rebuilt[rebuilt.length - 1]!.role === 'assistant' ? 'user' : 'assistant',
      content: 'Continuing from the compacted context above.',
    });
  }
  rebuilt.push(...tail);

  const afterTokens = estimatePromptOverheadTokens(systemPrompt, tools) + estimateMessagesTokens(rebuilt);
  return {
    messages: rebuilt,
    compacted: true,
    strategy,
    beforeTokens,
    afterTokens,
    summarizedTurns: evicted.length,
    summaryText,
  };
}

/**
 * Prepares initial conversation history without hard-dropping turns at 16.
 * Keeps all history when it fits within 45% of effective contextWindow;
 * summarizes only the older prefix when history exceeds budget.
 */
export async function prepareInitialMessages(params: {
  history?: Array<{ role: 'user' | 'assistant'; content: string }>;
  prompt: string;
  systemPrompt: string;
  tools: LlmToolDef[];
  contextWindow: number;
  summarizeFn?: (transcript: string) => Promise<string | null>;
}): Promise<{ messages: LlmMessage[]; compactedHistoryTurns: number; summaryText?: string }> {
  const rawHistory: LlmMessage[] = Array.isArray(params.history)
    ? params.history
        .filter((h) => h && typeof h.content === 'string' && h.content.trim().length > 0 && (h.role === 'user' || h.role === 'assistant'))
        .map((h) => ({ role: h.role, content: h.content }))
    : [];

  const allMessages: LlmMessage[] = [...rawHistory, { role: 'user', content: params.prompt }];
  const reserve = DEFAULT_OUTPUT_RESERVE_TOKENS;
  const effectiveBudget = Math.max(8_000, params.contextWindow - reserve);
  const historyBudget = Math.floor(effectiveBudget * 0.55);

  const totalTokens = estimatePromptOverheadTokens(params.systemPrompt, params.tools) + estimateMessagesTokens(allMessages);
  if (totalTokens <= historyBudget || rawHistory.length <= 6) {
    return { messages: allMessages, compactedHistoryTurns: 0 };
  }

  // Keep the most recent turns that fit inside 35% of effective budget, summarize older prefix
  const tailBudget = Math.floor(effectiveBudget * 0.30);
  let tailStart = rawHistory.length;
  let acc = 0;
  for (let i = rawHistory.length - 1; i >= 0; i--) {
    const t = estimateMessageTokens(rawHistory[i]!);
    if (acc + t > tailBudget && rawHistory.length - i >= 4) break;
    acc += t;
    tailStart = i;
  }

  if (tailStart <= 1) {
    return { messages: allMessages, compactedHistoryTurns: 0 };
  }

  const evicted = rawHistory.slice(0, tailStart);
  const tail = rawHistory.slice(tailStart);

  let summaryText = '';
  if (params.summarizeFn) {
    try {
      const llmSummary = await params.summarizeFn(formatMessagesForSummary(evicted));
      if (llmSummary && llmSummary.trim().length > 20) {
        summaryText = llmSummary.trim();
      }
    } catch {}
  }
  if (!summaryText) {
    summaryText = buildDeterministicSummary(evicted);
  }

  const rebuilt: LlmMessage[] = [
    { role: 'user', content: `<EARLIER_SESSION_SUMMARY>\n${summaryText}\n</EARLIER_SESSION_SUMMARY>` },
  ];
  if (tail.length > 0 && tail[0]!.role === 'user') {
    rebuilt.push({ role: 'assistant', content: 'Understood. I have the context from earlier in this session.' });
  }
  rebuilt.push(...tail);
  if (rebuilt[rebuilt.length - 1]!.role === 'user') {
    rebuilt.push({ role: 'assistant', content: 'Ready for your next instruction.' });
  }
  rebuilt.push({ role: 'user', content: params.prompt });

  return {
    messages: rebuilt,
    compactedHistoryTurns: evicted.length,
    summaryText,
  };
}

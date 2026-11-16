/**
 * Unified Streaming LLM Client for Gram
 *
 * Supports:
 * - Amazon Bedrock (`ConverseStreamCommand` via `@aws-sdk/client-bedrock-runtime`)
 * - OpenRouter (`https://openrouter.ai/api/v1/chat/completions` SSE streaming)
 */

import {
  BedrockRuntimeClient,
  ConverseStreamCommand,
  type ContentBlock,
  type Message,
  type Tool,
} from '@aws-sdk/client-bedrock-runtime';
import { getActivePrivacyMode, markModelUnavailable, type ModelProfile, type PrivacyMode } from './router';
import { redactSecretText } from './secretGuard';

export type LlmRole = 'user' | 'assistant';

export type LlmContentBlock =
  | { type: 'text'; text: string }
  | { type: 'tool_use'; id: string; name: string; input: Record<string, unknown> }
  | { type: 'tool_result'; toolUseId: string; content: string; isError?: boolean };

export interface LlmMessage {
  role: LlmRole;
  content: string | LlmContentBlock[];
}

export interface LlmToolDef {
  name: string;
  description: string;
  inputSchema: Record<string, unknown>;
}

export interface LlmToolCall {
  id: string;
  name: string;
  input: Record<string, unknown>;
}

export interface LlmStreamCallbacks {
  onThinkingDelta?: (delta: string) => void;
  onTextDelta?: (delta: string) => void;
}

export interface LlmTurnResult {
  text: string;
  thinking: string;
  toolCalls: LlmToolCall[];
  stopReason: string;
  usage?: {
    inputTokens: number;
    outputTokens: number;
  };
}

export class LlmAuthError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'LlmAuthError';
  }
}

function findPartialSuffixLength(text: string, tag: string): number {
  const maxCheck = Math.min(text.length, tag.length - 1);
  for (let len = maxCheck; len > 0; len--) {
    if (text.endsWith(tag.slice(0, len))) {
      return len;
    }
  }
  return 0;
}

export class ThinkingTagStreamSplitter {
  private inThinking = false;
  private buffer = '';

  constructor(
    private readonly onThinking: (delta: string) => void,
    private readonly onText: (delta: string) => void
  ) {}

  public push(chunk: string): void {
    this.buffer += chunk;
    while (this.buffer.length > 0) {
      if (!this.inThinking) {
        const openIdx = this.buffer.indexOf('<thinking>');
        if (openIdx >= 0) {
          const before = this.buffer.slice(0, openIdx);
          if (before) this.onText(before);
          this.buffer = this.buffer.slice(openIdx + '<thinking>'.length);
          this.inThinking = true;
          continue;
        }
        const hold = findPartialSuffixLength(this.buffer, '<thinking>');
        const safe = this.buffer.slice(0, this.buffer.length - hold);
        if (safe) this.onText(safe);
        this.buffer = this.buffer.slice(this.buffer.length - hold);
        break;
      } else {
        const closeIdx = this.buffer.indexOf('</thinking>');
        if (closeIdx >= 0) {
          const inner = this.buffer.slice(0, closeIdx);
          if (inner) this.onThinking(inner);
          this.buffer = this.buffer.slice(closeIdx + '</thinking>'.length).replace(/^\s+/, '');
          this.inThinking = false;
          continue;
        }
        const hold = findPartialSuffixLength(this.buffer, '</thinking>');
        const safe = this.buffer.slice(0, this.buffer.length - hold);
        if (safe) this.onThinking(safe);
        this.buffer = this.buffer.slice(this.buffer.length - hold);
        break;
      }
    }
  }

  public flush(): void {
    if (!this.buffer) return;
    if (this.inThinking) {
      this.onThinking(this.buffer);
    } else {
      this.onText(this.buffer);
    }
    this.buffer = '';
  }
}

let cachedBedrockClient: BedrockRuntimeClient | null = null;

function getBedrockClient(): BedrockRuntimeClient {
  if (!cachedBedrockClient) {
    const region = process.env.AWS_REGION || process.env.AWS_DEFAULT_REGION || 'us-east-1';
    cachedBedrockClient = new BedrockRuntimeClient({ region });
  }
  return cachedBedrockClient;
}

function toBedrockMessages(messages: LlmMessage[]): Message[] {
  return messages.map((msg) => {
    if (typeof msg.content === 'string') {
      return {
        role: msg.role,
        content: [{ text: msg.content }],
      };
    }
    const blocks: ContentBlock[] = msg.content.map((block) => {
      if (block.type === 'text') {
        return { text: block.text };
      }
      if (block.type === 'tool_use') {
        return {
          toolUse: {
            toolUseId: block.id,
            name: block.name,
            input: block.input as any,
          },
        };
      }
      return {
        toolResult: {
          toolUseId: block.toolUseId,
          content: [{ text: block.content }],
          status: block.isError ? 'error' : 'success',
        },
      };
    });
    return {
      role: msg.role,
      content: blocks,
    };
  });
}

function toBedrockTools(tools: LlmToolDef[]): Tool[] {
  return tools.map((tool) => ({
    toolSpec: {
      name: tool.name,
      description: tool.description,
      inputSchema: { json: tool.inputSchema as any },
    },
  }));
}

async function streamBedrockTurn(params: {
  model: ModelProfile;
  systemPrompt: string;
  messages: LlmMessage[];
  tools: LlmToolDef[];
  effort?: 'low' | 'medium' | 'high';
  signal?: AbortSignal;
  callbacks?: LlmStreamCallbacks;
}): Promise<LlmTurnResult> {
  const client = getBedrockClient();
  const maxTokens = params.effort === 'high' ? 8192 : params.effort === 'low' ? 2048 : 4096;

  const sendCommand = async (modelId: string) => {
    const command = new ConverseStreamCommand({
      modelId,
      system: params.systemPrompt ? [{ text: params.systemPrompt }] : undefined,
      messages: toBedrockMessages(params.messages),
      toolConfig: params.tools.length > 0 ? { tools: toBedrockTools(params.tools) } : undefined,
      inferenceConfig: {
        maxTokens,
        temperature: 0.2,
      },
    });
    return client.send(command, { abortSignal: params.signal });
  };

  let response;
  try {
    response = await sendCommand(params.model.id);
  } catch (err: any) {
    const fallbackModelId = process.env.BEDROCK_MODEL_ID;
    const code = String(err?.name || '');
    const msg = String(err?.message || err || '');
    if (
      fallbackModelId &&
      fallbackModelId !== params.model.id &&
      (code.includes('ValidationException') ||
        code.includes('ResourceNotFoundException') ||
        code.includes('AccessDeniedException') ||
        msg.includes('not available for this account') ||
        msg.includes('model identifier is invalid'))
    ) {
      markModelUnavailable(params.model.id);
      response = await sendCommand(fallbackModelId);
    } else {
      if (
        code.includes('Credentials') ||
        code.includes('Expired') ||
        code.includes('Unauthorized') ||
        code.includes('AccessDenied') ||
        msg.includes('expired') ||
        msg.includes('credentials') ||
        msg.includes('aws login') ||
        msg.includes('SSO')
      ) {
        throw new LlmAuthError(
          `AWS Bedrock authentication failed (${msg}). Run \`aws login\` in your terminal or set \`OPENROUTER_API_KEY\` in \`.env\`.`
        );
      }
      throw err;
    }
  }

  let text = '';
  let thinking = '';
  let stopReason = 'end_turn';
  let usage: { inputTokens: number; outputTokens: number } | undefined;

  const toolUseByIndex = new Map<number, { id: string; name: string; inputJson: string }>();
  const splitter = new ThinkingTagStreamSplitter(
    (delta) => {
      thinking += delta;
      params.callbacks?.onThinkingDelta?.(delta);
    },
    (delta) => {
      text += delta;
      params.callbacks?.onTextDelta?.(delta);
    }
  );

  if (response.stream) {
    for await (const event of response.stream) {
      if (params.signal?.aborted) break;

      if (event.contentBlockStart?.start?.toolUse) {
        const idx = event.contentBlockStart.contentBlockIndex ?? toolUseByIndex.size;
        const tu = event.contentBlockStart.start.toolUse;
        toolUseByIndex.set(idx, {
          id: tu.toolUseId || `tu-${Date.now()}-${idx}`,
          name: tu.name || 'unknown',
          inputJson: '',
        });
      }

      if (event.contentBlockDelta?.delta) {
        const idx = event.contentBlockDelta.contentBlockIndex ?? 0;
        const delta = event.contentBlockDelta.delta;

        if (delta.text) {
          splitter.push(delta.text);
        }

        const reasoningText = (delta as any).reasoningContent?.text;
        if (typeof reasoningText === 'string' && reasoningText.length > 0) {
          thinking += reasoningText;
          params.callbacks?.onThinkingDelta?.(reasoningText);
        }

        if (delta.toolUse?.input) {
          const entry = toolUseByIndex.get(idx);
          if (entry) {
            entry.inputJson += delta.toolUse.input;
          }
        }
      }

      if (event.messageStop?.stopReason) {
        stopReason = event.messageStop.stopReason;
      }

      if (event.metadata?.usage) {
        usage = {
          inputTokens: event.metadata.usage.inputTokens ?? 0,
          outputTokens: event.metadata.usage.outputTokens ?? 0,
        };
      }
    }
  }
  splitter.flush();

  const toolCalls: LlmToolCall[] = [];
  for (const entry of toolUseByIndex.values()) {
    let parsed: Record<string, unknown> = {};
    try {
      parsed = entry.inputJson.trim() ? JSON.parse(entry.inputJson) : {};
    } catch {
      parsed = { raw: entry.inputJson };
    }
    toolCalls.push({ id: entry.id, name: entry.name, input: parsed });
  }

  return { text, thinking, toolCalls, stopReason, usage };
}

function toOpenAiMessages(systemPrompt: string, messages: LlmMessage[]): any[] {
  const out: any[] = [];
  if (systemPrompt) {
    out.push({ role: 'system', content: systemPrompt });
  }
  for (const msg of messages) {
    if (typeof msg.content === 'string') {
      out.push({ role: msg.role, content: msg.content });
      continue;
    }

    if (msg.role === 'assistant') {
      const textParts = msg.content.filter((b) => b.type === 'text').map((b) => (b as any).text).join('');
      const toolUses = msg.content.filter((b) => b.type === 'tool_use') as Array<
        Extract<LlmContentBlock, { type: 'tool_use' }>
      >;
      out.push({
        role: 'assistant',
        content: textParts || null,
        tool_calls:
          toolUses.length > 0
            ? toolUses.map((tu) => ({
                id: tu.id,
                type: 'function',
                function: { name: tu.name, arguments: JSON.stringify(tu.input) },
              }))
            : undefined,
      });
    } else {
      const toolResults = msg.content.filter((b) => b.type === 'tool_result') as Array<
        Extract<LlmContentBlock, { type: 'tool_result' }>
      >;
      if (toolResults.length > 0) {
        for (const tr of toolResults) {
          out.push({
            role: 'tool',
            tool_call_id: tr.toolUseId,
            content: tr.content,
          });
        }
      } else {
        const textParts = msg.content.filter((b) => b.type === 'text').map((b) => (b as any).text).join('\n');
        out.push({ role: 'user', content: textParts });
      }
    }
  }
  return out;
}

async function streamOpenRouterTurn(params: {
  model: ModelProfile & {
    byokConfig?: { provider: string; modelId: string; baseUrl?: string; apiKey?: string };
  };
  systemPrompt: string;
  messages: LlmMessage[];
  tools: LlmToolDef[];
  effort?: 'low' | 'medium' | 'high';
  signal?: AbortSignal;
  callbacks?: LlmStreamCallbacks;
}): Promise<LlmTurnResult> {
  const byok = params.model.byokConfig;
  const apiKey =
    byok?.apiKey?.trim() ||
    (byok?.provider === 'openrouter' || !byok ? process.env.OPENROUTER_API_KEY : undefined);
  if (!apiKey && byok?.provider !== 'ollama') {
    throw new LlmAuthError(
      byok
        ? `API key is missing for BYOK model "${params.model.name}". Add it in Settings → Models.`
        : 'OPENROUTER_API_KEY is not set in `.env`. Add `OPENROUTER_API_KEY` or select a Bedrock model after running `aws login`.'
    );
  }

  const baseUrl = (byok?.baseUrl || 'https://openrouter.ai/api/v1').replace(/\/+$/, '');
  const upstreamModelId = byok?.modelId || params.model.id;
  const maxTokens = params.effort === 'high' ? 8192 : params.effort === 'low' ? 2048 : 4096;
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    'HTTP-Referer': 'http://localhost:7337',
    'X-Title': 'Gram Studio',
  };
  if (apiKey) {
    headers.Authorization = `Bearer ${apiKey}`;
  }

  const res = await fetch(`${baseUrl}/chat/completions`, {
    method: 'POST',
    headers,
    body: JSON.stringify({
      model: upstreamModelId,
      messages: toOpenAiMessages(params.systemPrompt, params.messages),
      tools:
        params.tools.length > 0
          ? params.tools.map((t) => ({
              type: 'function',
              function: {
                name: t.name,
                description: t.description,
                parameters: t.inputSchema,
              },
            }))
          : undefined,
      stream: true,
      max_tokens: maxTokens,
      temperature: 0.2,
    }),
    signal: params.signal,
  });

  if (res.status === 401 || res.status === 403) {
    throw new LlmAuthError(`OpenRouter authentication failed (${res.status}). Check OPENROUTER_API_KEY.`);
  }
  if (!res.ok || !res.body) {
    const bodyText = await res.text().catch(() => '');
    throw new Error(`OpenRouter request failed (${res.status}): ${bodyText.slice(0, 300)}`);
  }

  let text = '';
  let thinking = '';
  let stopReason = 'stop';
  const toolCallsByIndex = new Map<number, { id: string; name: string; argsJson: string }>();

  const reader = res.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  while (true) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });

    let newlineIdx: number;
    while ((newlineIdx = buffer.indexOf('\n')) >= 0) {
      const line = buffer.slice(0, newlineIdx).trim();
      buffer = buffer.slice(newlineIdx + 1);
      if (!line.startsWith('data:')) continue;
      const payload = line.slice(5).trim();
      if (!payload || payload === '[DONE]') continue;

      let parsed: any;
      try {
        parsed = JSON.parse(payload);
      } catch {
        continue;
      }

      const choice = parsed?.choices?.[0];
      if (!choice) continue;
      if (choice.finish_reason) stopReason = choice.finish_reason;

      const delta = choice.delta;
      if (!delta) continue;

      const reasoningDelta = delta.reasoning || delta.reasoning_content;
      if (typeof reasoningDelta === 'string' && reasoningDelta.length > 0) {
        thinking += reasoningDelta;
        params.callbacks?.onThinkingDelta?.(reasoningDelta);
      }

      if (typeof delta.content === 'string' && delta.content.length > 0) {
        text += delta.content;
        params.callbacks?.onTextDelta?.(delta.content);
      }

      if (Array.isArray(delta.tool_calls)) {
        for (const tc of delta.tool_calls) {
          const idx = typeof tc.index === 'number' ? tc.index : 0;
          const existing = toolCallsByIndex.get(idx) || {
            id: tc.id || `tc-${Date.now()}-${idx}`,
            name: tc.function?.name || '',
            argsJson: '',
          };
          if (tc.id) existing.id = tc.id;
          if (tc.function?.name) existing.name = tc.function.name;
          if (tc.function?.arguments) existing.argsJson += tc.function.arguments;
          toolCallsByIndex.set(idx, existing);
        }
      }
    }
  }

  const toolCalls: LlmToolCall[] = [];
  for (const entry of toolCallsByIndex.values()) {
    let input: Record<string, unknown> = {};
    try {
      input = entry.argsJson.trim() ? JSON.parse(entry.argsJson) : {};
    } catch {
      input = { raw: entry.argsJson };
    }
    toolCalls.push({ id: entry.id, name: entry.name, input });
  }

  return {
    text,
    thinking,
    toolCalls,
    stopReason: toolCalls.length > 0 ? 'tool_use' : stopReason,
  };
}

async function streamLyraGatewayTurn(params: {
  model: ModelProfile & {
    byokConfig?: { provider: string; modelId: string; baseUrl?: string; apiKey?: string };
  };
  systemPrompt: string;
  messages: LlmMessage[];
  tools: LlmToolDef[];
  effort?: 'low' | 'medium' | 'high';
  privacyMode?: PrivacyMode;
  signal?: AbortSignal;
  callbacks?: LlmStreamCallbacks;
}): Promise<LlmTurnResult> {
  const lyraUrl = (process.env.LYRA_HUB_URL || 'http://127.0.0.1:3001').replace(/\/+$/, '');
  const privacyMode = params.privacyMode || getActivePrivacyMode();
  const headers: Record<string, string> = { 'Content-Type': 'application/json' };
  const apiKey = process.env.LYRA_GATEWAY_API_KEY;
  if (apiKey) {
    headers['x-lyra-api-key'] = apiKey;
    headers.Authorization = `Bearer ${apiKey}`;
  }

  const provider =
    params.model.byokConfig?.provider === 'ollama'
      ? 'openai-compat'
      : params.model.provider;

  const res = await fetch(`${lyraUrl}/api/gateway/llm/stream`, {
    method: 'POST',
    headers,
    body: JSON.stringify({
      model: params.model.id,
      provider,
      privacyMode,
      effort: params.effort,
      supportsPromptCaching: params.model.supportsPromptCaching,
      systemPrompt: redactSecretText(params.systemPrompt),
      messages: params.messages,
      tools: params.tools,
      byokConfig: params.model.byokConfig,
    }),
    signal: params.signal,
  });

  if (!res.ok || !res.body) {
    const errText = await res.text().catch(() => '');
    if (res.status === 401 || res.status === 403) {
      throw new LlmAuthError(`Lyra Gateway authentication failed (${res.status}): ${errText.slice(0, 200)}`);
    }
    throw new Error(`Lyra Gateway error (${res.status}): ${errText.slice(0, 300)}`);
  }

  const reader = res.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let finalResult: LlmTurnResult | null = null;

  while (true) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    const lines = buffer.split('\n');
    buffer = lines.pop() ?? '';
    for (const rawLine of lines) {
      const line = rawLine.trim();
      if (!line.startsWith('data:')) continue;
      const payload = line.slice(5).trim();
      if (!payload) continue;
      const event = JSON.parse(payload);
      if (event.type === 'text_delta' && typeof event.delta === 'string') {
        params.callbacks?.onTextDelta?.(event.delta);
      } else if (event.type === 'thinking_delta' && typeof event.delta === 'string') {
        params.callbacks?.onThinkingDelta?.(event.delta);
      } else if (event.type === 'done' && event.result) {
        finalResult = event.result as LlmTurnResult;
      } else if (event.type === 'error') {
        throw new Error(event.message || 'Lyra Gateway stream error');
      }
    }
  }

  if (!finalResult) {
    throw new Error('Lyra Gateway stream ended without a completion event.');
  }
  return finalResult;
}

export async function streamLlmTurn(params: {
  model: ModelProfile & {
    byokConfig?: { provider: string; modelId: string; baseUrl?: string; apiKey?: string };
  };
  systemPrompt: string;
  messages: LlmMessage[];
  tools: LlmToolDef[];
  effort?: 'low' | 'medium' | 'high';
  privacyMode?: PrivacyMode;
  signal?: AbortSignal;
  callbacks?: LlmStreamCallbacks;
}): Promise<LlmTurnResult> {
  const privacyMode = params.privacyMode || getActivePrivacyMode();
  const useDirect = process.env.GRAM_DIRECT_LLM === 'true';

  if (!useDirect) {
    try {
      return await streamLyraGatewayTurn({ ...params, privacyMode });
    } catch (err: any) {
      // In local-only mode, allow direct localhost Ollama fallback if Lyra is not running locally
      const isLocalOllama = params.model.byokConfig?.provider === 'ollama';
      const isNetworkDown = err?.code === 'ECONNREFUSED' || String(err?.message || '').includes('fetch failed');
      if (!(privacyMode === 'local-only' && isLocalOllama && isNetworkDown)) {
        throw err;
      }
    }
  }

  if (privacyMode === 'zdr' && params.model.contributorWarning) {
    throw new Error(`Model "${params.model.id}" is blocked in ZDR privacy mode: ${params.model.contributorWarning}`);
  }
  if (privacyMode === 'local-only' && params.model.byokConfig?.provider !== 'ollama') {
    throw new Error(`Privacy policy "local-only" blocks non-local model "${params.model.id}".`);
  }

  if (params.model.provider === 'openrouter') {
    return streamOpenRouterTurn(params);
  }
  return streamBedrockTurn(params);
}

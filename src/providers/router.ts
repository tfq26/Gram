/**
 * Gram + Lyra Unified Model Registry & Auto Router
 *
 * Strictly allowlists:
 * - Amazon Bedrock: Latest GPT, Claude, Nova, and coding-capable Llama models
 * - OpenRouter: Verified coding models (DeepSeek, Qwen, Mistral, GLM, Kimi, Gemini 4, Muse Spark, Grok)
 */

export type ModelProvider = 'bedrock' | 'openrouter';
export type PrivacyMode = 'standard' | 'zdr' | 'local-only';
export type ModelFamily =
  | 'anthropic'
  | 'openai'
  | 'amazon'
  | 'meta'
  | 'deepseek'
  | 'qwen'
  | 'mistral'
  | 'glm'
  | 'kimi'
  | 'gemini'
  | 'xai';

export interface ModelProfile {
  id: string;
  name: string;
  provider: ModelProvider;
  family: ModelFamily;
  inputPricePerM: number;
  outputPricePerM: number;
  contextWindow: number;
  supportsPromptCaching: boolean;
  contributorWarning?: string;
  capabilities: {
    coding: number;
    reasoning: number;
    speed: number;
  };
}

export const AUTO_MODEL_ID = 'auto';

export const MODEL_REGISTRY: Record<string, ModelProfile> = {
  // ── Amazon Bedrock (10 models) ───────────────────────────────────────────
  'us.amazon.nova-2-lite-v1:0': {
    id: 'us.amazon.nova-2-lite-v1:0',
    name: 'Amazon Nova 2 Lite',
    provider: 'bedrock',
    family: 'amazon',
    inputPricePerM: 0.06,
    outputPricePerM: 0.24,
    contextWindow: 300_000,
    supportsPromptCaching: true,
    capabilities: { coding: 80, reasoning: 76, speed: 98 },
  },
  'us.meta.llama4-maverick-17b-instruct-v1:0': {
    id: 'us.meta.llama4-maverick-17b-instruct-v1:0',
    name: 'Llama 4 Maverick 17B',
    provider: 'bedrock',
    family: 'meta',
    inputPricePerM: 0.24,
    outputPricePerM: 0.97,
    contextWindow: 128_000,
    supportsPromptCaching: false,
    capabilities: { coding: 83, reasoning: 81, speed: 94 },
  },
  'us.meta.llama3-3-70b-instruct-v1:0': {
    id: 'us.meta.llama3-3-70b-instruct-v1:0',
    name: 'Llama 3.3 70B',
    provider: 'bedrock',
    family: 'meta',
    inputPricePerM: 0.72,
    outputPricePerM: 0.72,
    contextWindow: 128_000,
    supportsPromptCaching: false,
    capabilities: { coding: 85, reasoning: 84, speed: 88 },
  },
  'us.amazon.nova-2-pro-v1:0': {
    id: 'us.amazon.nova-2-pro-v1:0',
    name: 'Amazon Nova 2 Pro',
    provider: 'bedrock',
    family: 'amazon',
    inputPricePerM: 0.8,
    outputPricePerM: 3.2,
    contextWindow: 300_000,
    supportsPromptCaching: true,
    capabilities: { coding: 89, reasoning: 88, speed: 91 },
  },
  'us.openai.gpt-6-luna': {
    id: 'us.openai.gpt-6-luna',
    name: 'GPT-6 Luna',
    provider: 'bedrock',
    family: 'openai',
    inputPricePerM: 0.25,
    outputPricePerM: 1.0,
    contextWindow: 256_000,
    supportsPromptCaching: true,
    capabilities: { coding: 88, reasoning: 86, speed: 97 },
  },
  'us.anthropic.claude-haiku-5-5': {
    id: 'us.anthropic.claude-haiku-5-5',
    name: 'Claude Haiku 5.5',
    provider: 'bedrock',
    family: 'anthropic',
    inputPricePerM: 0.8,
    outputPricePerM: 4.0,
    contextWindow: 200_000,
    supportsPromptCaching: true,
    capabilities: { coding: 89, reasoning: 85, speed: 96 },
  },
  'us.openai.gpt-6.1-terra': {
    id: 'us.openai.gpt-6.1-terra',
    name: 'GPT-6.1 Terra',
    provider: 'bedrock',
    family: 'openai',
    inputPricePerM: 1.5,
    outputPricePerM: 6.0,
    contextWindow: 1_000_000,
    supportsPromptCaching: true,
    capabilities: { coding: 94, reasoning: 93, speed: 90 },
  },
  'us.anthropic.claude-sonnet-5-5': {
    id: 'us.anthropic.claude-sonnet-5-5',
    name: 'Claude Sonnet 5.5',
    provider: 'bedrock',
    family: 'anthropic',
    inputPricePerM: 3.0,
    outputPricePerM: 15.0,
    contextWindow: 200_000,
    supportsPromptCaching: true,
    capabilities: { coding: 98, reasoning: 97, speed: 86 },
  },
  'us.openai.gpt-6.1-sol': {
    id: 'us.openai.gpt-6.1-sol',
    name: 'GPT-6.1 Sol',
    provider: 'bedrock',
    family: 'openai',
    inputPricePerM: 3.5,
    outputPricePerM: 14.0,
    contextWindow: 400_000,
    supportsPromptCaching: true,
    capabilities: { coding: 98, reasoning: 97, speed: 84 },
  },
  'us.anthropic.claude-opus-5-5': {
    id: 'us.anthropic.claude-opus-5-5',
    name: 'Claude Opus 5.5',
    provider: 'bedrock',
    family: 'anthropic',
    inputPricePerM: 15.0,
    outputPricePerM: 75.0,
    contextWindow: 200_000,
    supportsPromptCaching: true,
    capabilities: { coding: 99, reasoning: 100, speed: 74 },
  },

  // ── OpenRouter (17 verified coding models) ───────────────────────────────
  'deepseek/deepseek-v4.1-flash': {
    id: 'deepseek/deepseek-v4.1-flash',
    name: 'DeepSeek V4.1 Flash',
    provider: 'openrouter',
    family: 'deepseek',
    inputPricePerM: 0.05,
    outputPricePerM: 0.3,
    contextWindow: 1_048_576,
    supportsPromptCaching: true,
    capabilities: { coding: 88, reasoning: 86, speed: 97 },
  },
  'qwen/qwen3-coder-30b-a3b-instruct': {
    id: 'qwen/qwen3-coder-30b-a3b-instruct',
    name: 'Qwen3 Coder 30B',
    provider: 'openrouter',
    family: 'qwen',
    inputPricePerM: 0.15,
    outputPricePerM: 0.6,
    contextWindow: 262_144,
    supportsPromptCaching: false,
    capabilities: { coding: 86, reasoning: 82, speed: 96 },
  },
  'mistralai/mistral-small-2603': {
    id: 'mistralai/mistral-small-2603',
    name: 'Mistral Small 4',
    provider: 'openrouter',
    family: 'mistral',
    inputPricePerM: 0.15,
    outputPricePerM: 0.6,
    contextWindow: 128_000,
    supportsPromptCaching: false,
    capabilities: { coding: 83, reasoning: 81, speed: 95 },
  },
  'qwen/qwen3-coder-flash': {
    id: 'qwen/qwen3-coder-flash',
    name: 'Qwen3 Coder Flash',
    provider: 'openrouter',
    family: 'qwen',
    inputPricePerM: 0.195,
    outputPricePerM: 0.975,
    contextWindow: 1_000_000,
    supportsPromptCaching: true,
    capabilities: { coding: 90, reasoning: 87, speed: 94 },
  },
  'qwen/qwen3-coder': {
    id: 'qwen/qwen3-coder',
    name: 'Qwen3 Coder 480B',
    provider: 'openrouter',
    family: 'qwen',
    inputPricePerM: 0.3,
    outputPricePerM: 1.0,
    contextWindow: 262_144,
    supportsPromptCaching: true,
    capabilities: { coding: 94, reasoning: 91, speed: 87 },
  },
  'mistralai/codestral-latest': {
    id: 'mistralai/codestral-latest',
    name: 'Codestral',
    provider: 'openrouter',
    family: 'mistral',
    inputPricePerM: 0.3,
    outputPricePerM: 0.9,
    contextWindow: 256_000,
    supportsPromptCaching: false,
    capabilities: { coding: 91, reasoning: 85, speed: 93 },
  },
  'z-ai/glm-5.3-flash': {
    id: 'z-ai/glm-5.3-flash',
    name: 'GLM 5.3 Flash',
    provider: 'openrouter',
    family: 'glm',
    inputPricePerM: 0.37,
    outputPricePerM: 1.25,
    contextWindow: 200_000,
    supportsPromptCaching: true,
    capabilities: { coding: 91, reasoning: 88, speed: 94 },
  },
  'mistralai/devstral-2512': {
    id: 'mistralai/devstral-2512',
    name: 'Devstral 2',
    provider: 'openrouter',
    family: 'mistral',
    inputPricePerM: 0.4,
    outputPricePerM: 2.0,
    contextWindow: 256_000,
    supportsPromptCaching: false,
    capabilities: { coding: 93, reasoning: 90, speed: 88 },
  },
  'mistralai/mistral-large-4-0': {
    id: 'mistralai/mistral-large-4-0',
    name: 'Mistral Large 4',
    provider: 'openrouter',
    family: 'mistral',
    inputPricePerM: 0.5,
    outputPricePerM: 1.5,
    contextWindow: 256_000,
    supportsPromptCaching: false,
    capabilities: { coding: 92, reasoning: 92, speed: 86 },
  },
  'moonshotai/kimi-k2.6': {
    id: 'moonshotai/kimi-k2.6',
    name: 'Kimi K2.6',
    provider: 'openrouter',
    family: 'kimi',
    inputPricePerM: 0.55,
    outputPricePerM: 2.4,
    contextWindow: 262_144,
    supportsPromptCaching: false,
    capabilities: { coding: 92, reasoning: 93, speed: 85 },
  },
  'z-ai/glm-5': {
    id: 'z-ai/glm-5',
    name: 'GLM-5',
    provider: 'openrouter',
    family: 'glm',
    inputPricePerM: 0.9,
    outputPricePerM: 3.6,
    contextWindow: 200_000,
    supportsPromptCaching: true,
    capabilities: { coding: 93, reasoning: 92, speed: 85 },
  },
  'x-ai/grok-4.20': {
    id: 'x-ai/grok-4.20',
    name: 'Grok 4.20',
    provider: 'openrouter',
    family: 'xai',
    inputPricePerM: 1.25,
    outputPricePerM: 2.5,
    contextWindow: 256_000,
    supportsPromptCaching: true,
    capabilities: { coding: 93, reasoning: 93, speed: 88 },
  },
  'meta/muse-spark-1.3': {
    id: 'meta/muse-spark-1.3',
    name: 'Muse Spark 1.3',
    provider: 'openrouter',
    family: 'meta',
    inputPricePerM: 1.25,
    outputPricePerM: 4.25,
    contextWindow: 1_000_000,
    supportsPromptCaching: true,
    contributorWarning: 'Avoid Contributor endpoints on private repos; prompts may be used for training.',
    capabilities: { coding: 95, reasoning: 94, speed: 88 },
  },
  'google/gemini-4-hydrogen': {
    id: 'google/gemini-4-hydrogen',
    name: 'Gemini 4 Hydrogen',
    provider: 'openrouter',
    family: 'gemini',
    inputPricePerM: 1.5,
    outputPricePerM: 7.5,
    contextWindow: 1_000_000,
    supportsPromptCaching: true,
    capabilities: { coding: 96, reasoning: 95, speed: 91 },
  },
  'google/gemini-4-argon': {
    id: 'google/gemini-4-argon',
    name: 'Gemini 4 Argon',
    provider: 'openrouter',
    family: 'gemini',
    inputPricePerM: 2.0,
    outputPricePerM: 10.0,
    contextWindow: 1_000_000,
    supportsPromptCaching: true,
    capabilities: { coding: 98, reasoning: 99, speed: 82 },
  },
  'deepseek/deepseek-v4-pro-0813': {
    id: 'deepseek/deepseek-v4-pro-0813',
    name: 'DeepSeek V4 Pro',
    provider: 'openrouter',
    family: 'deepseek',
    inputPricePerM: 0.1,
    outputPricePerM: 8.0,
    contextWindow: 256_000,
    supportsPromptCaching: true,
    capabilities: { coding: 95, reasoning: 97, speed: 81 },
  },
  'moonshotai/kimi-k3.1-pro': {
    id: 'moonshotai/kimi-k3.1-pro',
    name: 'Kimi K3.1 Pro',
    provider: 'openrouter',
    family: 'kimi',
    inputPricePerM: 1.5,
    outputPricePerM: 9.0,
    contextWindow: 262_144,
    supportsPromptCaching: false,
    capabilities: { coding: 96, reasoning: 98, speed: 79 },
  },
};

export interface RouteRequest {
  taskComplexity: 'simple' | 'moderate' | 'complex' | 'architecture';
  priority: 'cost' | 'balanced' | 'speed' | 'frontier' | 'planning';
  pinnedModelId?: string;
}

export function isAllowedModel(modelId: string): boolean {
  return modelId === AUTO_MODEL_ID || Boolean(MODEL_REGISTRY[modelId]);
}

export function listAllowedModels(): ModelProfile[] {
  return Object.values(MODEL_REGISTRY);
}

const unavailableModels = new Set<string>();

export function markModelUnavailable(modelId: string): void {
  if (modelId) unavailableModels.add(modelId);
}

export function isModelUnavailable(modelId: string): boolean {
  return unavailableModels.has(modelId);
}

export function clearUnavailableModels(): void {
  unavailableModels.clear();
}

export function getActivePrivacyMode(): PrivacyMode {
  const raw = (process.env.GRAM_PRIVACY_MODE || process.env.LYRA_PRIVACY_MODE || 'standard').toLowerCase();
  if (raw === 'zdr' || raw === 'local-only') return raw;
  return 'standard';
}

export function selectOptimalModel(request: RouteRequest & { privacyMode?: PrivacyMode }): ModelProfile {
  const privacyMode = request.privacyMode || getActivePrivacyMode();

  if (
    request.pinnedModelId &&
    request.pinnedModelId !== AUTO_MODEL_ID &&
    MODEL_REGISTRY[request.pinnedModelId] &&
    !unavailableModels.has(request.pinnedModelId)
  ) {
    const pinned = MODEL_REGISTRY[request.pinnedModelId]!;
    if (privacyMode === 'zdr' && pinned.contributorWarning) {
      throw new Error(`Model "${pinned.id}" is blocked in ZDR privacy mode: ${pinned.contributorWarning}`);
    }
    return pinned;
  }

  const allModels = listAllowedModels();
  const hasOpenRouter = Boolean(process.env.OPENROUTER_API_KEY);
  const providerFiltered = hasOpenRouter ? allModels : allModels.filter((m) => m.provider === 'bedrock');
  const privacyFiltered = privacyMode === 'zdr'
    ? providerFiltered.filter((m) => !m.contributorWarning)
    : providerFiltered;
  const availableModels = privacyFiltered.filter((m) => !unavailableModels.has(m.id));
  const models = availableModels.length > 0 ? availableModels : privacyFiltered;

  if (request.priority === 'cost' || request.taskComplexity === 'simple') {
    return models
      .slice()
      .sort((a, b) => (a.inputPricePerM + a.outputPricePerM) - (b.inputPricePerM + b.outputPricePerM))[0]!;
  }

  if (request.priority === 'speed') {
    return models
      .slice()
      .sort((a, b) => b.capabilities.speed - a.capabilities.speed)[0]!;
  }

  if (request.priority === 'planning') {
    const capable = models.filter((m) => m.capabilities.reasoning >= 9.0 && m.capabilities.coding >= 9.0);
    const pool = capable.length > 0 ? capable : models;
    return pool
      .slice()
      .sort((a, b) => {
        const priceA = Math.max(0.2, a.inputPricePerM + a.outputPricePerM * 0.5);
        const priceB = Math.max(0.2, b.inputPricePerM + b.outputPricePerM * 0.5);
        const valueA = (a.capabilities.reasoning * 0.65 + a.capabilities.coding * 0.35) / Math.log2(priceA + 2);
        const valueB = (b.capabilities.reasoning * 0.65 + b.capabilities.coding * 0.35) / Math.log2(priceB + 2);
        return valueB - valueA;
      })[0]!;
  }

  if (request.taskComplexity === 'architecture' || request.priority === 'frontier') {
    return models
      .slice()
      .sort((a, b) => (b.capabilities.coding + b.capabilities.reasoning) - (a.capabilities.coding + a.capabilities.reasoning))[0]!;
  }

  // Balanced Auto: prioritize coding + reasoning quality, using normalized cost only as a gentle tie-breaker
  return models
    .slice()
    .sort((a, b) => {
      const costPenaltyA = Math.min(1.5, (a.inputPricePerM + a.outputPricePerM * 0.5) * 0.04);
      const costPenaltyB = Math.min(1.5, (b.inputPricePerM + b.outputPricePerM * 0.5) * 0.04);
      const scoreA = a.capabilities.coding * 0.6 + a.capabilities.reasoning * 0.4 - costPenaltyA;
      const scoreB = b.capabilities.coding * 0.6 + b.capabilities.reasoning * 0.4 - costPenaltyB;
      return scoreB - scoreA;
    })[0]!;
}

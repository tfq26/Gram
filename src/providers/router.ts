/**
 * Gram model routing helpers.
 *
 * The model catalog itself (ids, prices, capability scores, aliases) is owned by Lyra and cached
 * locally by `modelCatalog.ts`. This module keeps the synchronous selection logic on top of it:
 * allowlist checks, privacy filtering, and the local `auto` heuristic used for cheap utility turns,
 * the CLI and direct mode. In gateway mode, `auto` requests are routed by Lyra itself.
 */

import { getCatalogModel, getCatalogState, listCatalogModels } from './modelCatalog';

export type ModelProvider = 'bedrock' | 'foundry' | 'openrouter';
export type PrivacyMode = 'standard' | 'zdr' | 'local-only';
export type WorkflowKind =
  | 'coding'
  | 'research'
  | 'long-context'
  | 'world-social'
  | 'triage-routing'
  | 'fast-utility';
export type ModelFamily =
  | 'anthropic'
  | 'openai'
  | 'microsoft'
  | 'amazon'
  | 'meta'
  | 'deepseek'
  | 'qwen'
  | 'mistral'
  | 'glm'
  | 'kimi'
  | 'gemini'
  | 'xai'
  | 'minimax'
  | 'nvidia'
  | 'cohere';

export interface ModelProfile {
  id: string;
  name: string;
  provider: ModelProvider;
  family: ModelFamily;
  workflows?: WorkflowKind[];
  inputPricePerM: number;
  outputPricePerM: number;
  contextWindow: number;
  supportsPromptCaching: boolean;
  contributorWarning?: string;
  autoEligible?: boolean;
  /** Legacy and provider-specific ids that resolve to this model (served by Lyra). */
  aliases?: string[];
  capabilities: {
    coding: number;
    reasoning: number;
    speed: number;
  };
}

export const AUTO_MODEL_ID = 'auto';

export interface RouteRequest {
  taskComplexity: 'simple' | 'moderate' | 'complex' | 'architecture';
  priority: 'cost' | 'balanced' | 'speed' | 'frontier' | 'planning';
  workflow?: WorkflowKind;
  pinnedModelId?: string;
}

/** Looks up a model by canonical id or any legacy alias (e.g. `us.amazon.nova-2-lite-v1:0`). */
export function getModelProfile(modelId: string): ModelProfile | undefined {
  return getCatalogModel(modelId);
}

/**
 * UX-level allowlist. Lyra's gateway is the authoritative check, so while only the built-in
 * fallback catalog is loaded (Lyra not yet reachable) unknown ids pass through rather than being
 * rejected against an incomplete list.
 */
export function isAllowedModel(modelId: string): boolean {
  if (modelId === AUTO_MODEL_ID) return true;
  if (getCatalogModel(modelId)) return true;
  return Boolean(modelId) && getCatalogState().source === 'builtin';
}

export function listAllowedModels(): ModelProfile[] {
  return listCatalogModels();
}

const unavailableModels = new Set<string>();

export function markModelUnavailable(modelId: string): void {
  if (modelId) unavailableModels.add(getCatalogModel(modelId)?.id ?? modelId);
}

export function isModelUnavailable(modelId: string): boolean {
  return unavailableModels.has(getCatalogModel(modelId)?.id ?? modelId);
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

  const pinned =
    request.pinnedModelId && request.pinnedModelId !== AUTO_MODEL_ID
      ? getCatalogModel(request.pinnedModelId)
      : undefined;
  if (pinned && !unavailableModels.has(pinned.id)) {
    if (privacyMode === 'zdr' && pinned.contributorWarning) {
      throw new Error(`Model "${pinned.id}" is blocked in ZDR privacy mode: ${pinned.contributorWarning}`);
    }
    return pinned;
  }

  const allModels = listAllowedModels();
  const hasOpenRouter = Boolean(process.env.OPENROUTER_API_KEY);
  const viaLyraGateway = process.env.GRAM_DIRECT_LLM !== 'true';
  const hasFoundry =
    viaLyraGateway ||
    Boolean(process.env.AZURE_FOUNDRY_ENDPOINT || process.env.LYRA_MANAGED_FOUNDRY_ENDPOINT);
  const providerFiltered = allModels.filter(
    (m) =>
      m.autoEligible !== false &&
      (m.provider === 'bedrock' ||
        (m.provider === 'openrouter' && hasOpenRouter) ||
        (m.provider === 'foundry' && hasFoundry))
  );
  const privacyFiltered = privacyMode === 'zdr'
    ? providerFiltered.filter((m) => !m.contributorWarning)
    : providerFiltered;
  const workflowFiltered = request.workflow
    ? privacyFiltered.filter((m) => m.workflows?.includes(request.workflow!))
    : [];
  const workflowPool = workflowFiltered.length > 0 ? workflowFiltered : privacyFiltered;
  const availableModels = workflowPool.filter((m) => !unavailableModels.has(m.id));
  const models = availableModels.length > 0 ? availableModels : workflowPool;

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
    // Scores are on a 0-100 scale.
    const capable = models.filter((m) => m.capabilities.reasoning >= 90 && m.capabilities.coding >= 90);
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

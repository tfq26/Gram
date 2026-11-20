/**
 * Gram Stateless Context Envelope & Provider-Neutral Intermediate Representation (IR)
 * 
 * Ensures context belongs to the PROJECT, not locked inside a specific provider's chat thread.
 * Translates between different model families (Anthropic Claude, OpenAI Codex, Google Gemini, DeepSeek/Ollama)
 * without losing reasoning state, pinned facts, or active plan progress.
 */

export type ModelFamily = 'anthropic' | 'openai' | 'gemini' | 'open-weights' | 'gram-native';

export interface PlanNode {
  id: string;
  title: string;
  status: 'pending' | 'in_progress' | 'completed' | 'failed';
  dependencies: string[];
}

export interface TrajectoryStep {
  sequence: number;
  role: 'user' | 'assistant' | 'tool';
  content: string;
  thinking?: string;
  toolCalls?: Array<{
    name: string;
    args: Record<string, unknown>;
    result?: unknown;
  }>;
  timestamp: string;
}

export interface ContextEnvelopeState {
  sessionId: string;
  workspacePath: string;
  activeModel: string;
  activeFamily: ModelFamily;
  pinnedFacts: Record<string, string>;
  activePlan: PlanNode[];
  trajectory: TrajectoryStep[];
  headroomCompressedCount: number;
}

export interface HandoffCapsule {
  sessionId: string;
  fromModelId: string;
  toModelId: string;
  fromFamily: ModelFamily;
  toFamily: ModelFamily;
  reason: string;
  pinnedFacts: Record<string, string>;
  activePlan: PlanNode[];
  recentTurns: TrajectoryStep[];
  createdAt: string;
}

export class ContextEnvelope {
  public state: ContextEnvelopeState;

  constructor(initial: Partial<ContextEnvelopeState> & { sessionId: string; workspacePath: string }) {
    this.state = {
      sessionId: initial.sessionId,
      workspacePath: initial.workspacePath,
      activeModel: initial.activeModel || 'us.anthropic.claude-sonnet-5',
      activeFamily: initial.activeFamily || 'anthropic',
      pinnedFacts: initial.pinnedFacts || {},
      activePlan: initial.activePlan || [],
      trajectory: initial.trajectory || [],
      headroomCompressedCount: initial.headroomCompressedCount || 0,
    };
  }

  /**
   * Detects the ModelFamily from a model ID string.
   */
  public static detectFamily(modelId: string): ModelFamily {
    const lower = modelId.toLowerCase();
    if (lower.includes('claude') || lower.includes('anthropic')) return 'anthropic';
    if (lower.includes('gpt') || lower.includes('codex') || lower.includes('openai')) return 'openai';
    if (lower.includes('gemini')) return 'gemini';
    if (lower.includes('qwen') || lower.includes('deepseek') || lower.includes('llama') || lower.includes('ollama')) return 'open-weights';
    return 'gram-native';
  }

  /**
   * Appends an event to the trajectory.
   */
  public appendTurn(step: Omit<TrajectoryStep, 'sequence' | 'timestamp'>): TrajectoryStep {
    const sequence = this.state.trajectory.length + 1;
    const fullStep: TrajectoryStep = {
      ...step,
      sequence,
      timestamp: new Date().toISOString(),
    };
    this.state.trajectory.push(fullStep);
    return fullStep;
  }

  /**
   * Pins a verified fact into the Project Context Graph.
   */
  public pinFact(key: string, fact: string): void {
    this.state.pinnedFacts[key] = fact;
  }

  /**
   * Updates plan progress.
   */
  public updatePlan(plan: PlanNode[]): void {
    this.state.activePlan = plan;
  }

  /**
   * Compiles the envelope into a Provider-Neutral System Context Block.
   */
  public compileSystemPrompt(): string {
    const facts = Object.entries(this.state.pinnedFacts)
      .map(([k, v]) => `• [${k}]: ${v}`)
      .join('\n');

    const plan = this.state.activePlan
      .map((p) => `• [${p.status.toUpperCase()}] (${p.id}) ${p.title}`)
      .join('\n');

    return `You are Gram v2, an autonomous agent-native coding partner.
You operate on workspace: ${this.state.workspacePath}

<PROJECT_CONTEXT_GRAPH>
${facts || 'No pinned facts yet.'}
</PROJECT_CONTEXT_GRAPH>

<IMPLEMENTATION_PLAN>
${plan || 'No active plan initialized.'}
</IMPLEMENTATION_PLAN>

<AGENT_SKILLS_DIRECTORY>
The following autonomous skills are available in your cognitive loop. Invoke them autonomously:
- env_doctor: Inspects toolchain health, port availability, and .env drift. Trigger before starting servers or when diagnosing runtime/connection issues.
- semantic_graph: Scans AST into Semantic URIs (gram://file#kind:symbol) and calculates call-site blast radius. Trigger before signature edits to prevent downstream breakages.
- device_mesh_connect: Inspects active peer execution hosts across Gram Connect. Trigger when delegating tasks across devices.
- code_mode: Single-turn programmatic execution via typed gram.* SDK. Trigger when multi-step edits or assertions are faster in 1 turn than conversational back-and-forth.
- hypothesis_race: Speculative parallel CoW branch racing. Trigger when testing competing bug fixes without dirtying the main working tree.
</AGENT_SKILLS_DIRECTORY>

<UI_CRAFT_CONSTITUTION>
When creating or modifying any UI component, HTML, CSS, or UX flow, you MUST strictly follow /design.md and NEVER introduce "vibe-coded" AI slop patterns:
- ZERO EMOJIS IN UI: Never use Unicode emojis (🚀, ✨, ⚡, 🔍, 📁, 🩺, 📱, ⚙️) or ASCII arrows (▼, ▲, ›, ✕, ☰) as icons. Use crisp 14px-16px monochrome 1.5px-stroke SVG vector icons (stroke="currentColor").
- ZERO CLICHE GRADIENTS OR GLOW BLOBS: Never use purple-to-blue/indigo gradients, gradient text headings (background-clip: text), floating blurred radial orbs, or neon box-shadows.
- ZERO SPARKLE PILLS OR AI BUZZWORDS: Never add "✨ AI-Powered" pills or marketing buzzwords ("supercharge", "unleash", "seamless", "elevate", "next-gen") or excessive em-dashes.
- HAIRLINE & SURFACE DISCIPLINE: Convey depth via 1px solid low-opacity hairlines (rgba(255,255,255,0.06)) and calibrated dark surfaces (#08090a -> #0f1012 -> #151619). Never use dashed borders on content cards or nest cards 3+ levels deep (card-ception).
- TYPOGRAPHY & MOTION PRECISION: Use tabular-nums on all numbers/ports/timestamps, text-wrap: balance on headings, appearance: none on custom selects, no hover scale/bounce transforms, and never use blocking browser alert()/confirm() popups.
</UI_CRAFT_CONSTITUTION>

Rules:
1. Always favor surgical, line-independent semantic edits.
2. Verify all modifications with the local verify/probe engine.
3. Keep changesets minimal and atomic.
4. Enforce all 90 anti-vibe-coding rules in design.md when authoring UI.`;
  }

  /**
   * Model-Adaptive Tool Projection:
   * Generates the tool schema that matches the active model's native post-training RLHF weights.
   */
  public getProjectedTools(targetFamily = this.state.activeFamily): any[] {
    const commonTools = [
      {
        name: 'env_doctor',
        description: 'Autonomous Environment Doctor: inspects toolchain health, open/blocked ports, and .env drift. Invoke when user asks to "verify setup" or when diagnosing runtime/network errors.',
        inputSchema: {
          type: 'object',
          properties: {
            ports: { type: 'array', items: { type: 'number' }, description: 'Ports to check for conflicts (e.g. [3000, 5173, 7337])' },
            checkEnv: { type: 'boolean', description: 'Whether to check for .env presence' },
          },
        },
      },
      {
        name: 'semantic_graph',
        description: 'Autonomous Semantic Graph: indexes AST into Semantic URIs and computes call-graph blast radius before modifying symbols. Invoke when planning refactors or checking downstream breakage risk.',
        inputSchema: {
          type: 'object',
          properties: {
            action: { type: 'string', enum: ['index', 'blast_radius', 'search_symbol', 'intent_blame'] },
            symbolUri: { type: 'string', description: 'Target Semantic URI' },
            proposedSignature: { type: 'string', description: 'Proposed new signature' },
          },
          required: ['action'],
        },
      },
      {
        name: 'device_mesh_connect',
        description: 'Autonomous Device Mesh: inspects online execution targets and peer devices across Gram Connect via AHP v2. Invoke when checking companion devices or distributed execution.',
        inputSchema: {
          type: 'object',
          properties: {
            action: { type: 'string', enum: ['list_devices', 'inspect_presence'] },
          },
        },
      },
      {
        name: 'code_mode',
        description: 'Code Mode: execute a programmatic TypeScript script with the typed `gram.*` SDK (gram.fs, gram.symbol, gram.probe, gram.verify). Invoke when multi-step operations are cleaner in a single turn.',
        inputSchema: {
          type: 'object',
          properties: {
            script: { type: 'string', description: 'TypeScript/JavaScript script to run' },
            layerId: { type: 'string', description: 'Virtual GramFS layer (defaults to main)' },
          },
          required: ['script'],
        },
      },
      {
        name: 'hypothesis_race',
        description: 'Speculative Hypothesis Race: forks isolated GramFS CoW branches in parallel to test competing bug fixes against a verify function, promoting only the winner. Invoke when facing ambiguous bugs.',
        inputSchema: {
          type: 'object',
          properties: {
            hypotheses: {
              type: 'array',
              items: {
                type: 'object',
                properties: {
                  name: { type: 'string' },
                  patchFile: { type: 'string' },
                  patchContent: { type: 'string' },
                },
              },
            },
          },
        },
      },
      {
        name: 'read_file',
        description: 'Read the contents of a source file.',
        inputSchema: {
          type: 'object',
          properties: {
            path: { type: 'string' },
            startLine: { type: 'number' },
            endLine: { type: 'number' },
          },
          required: ['path'],
        },
      },
    ];

    if (targetFamily === 'anthropic') {
      // Claude is trained on str_replace
      return [
        ...commonTools,
        {
          name: 'edit_file',
          description: 'Surgically replace target string block in a file with exact whitespace preservation.',
          inputSchema: {
            type: 'object',
            properties: {
              path: { type: 'string' },
              old_str: { type: 'string' },
              new_str: { type: 'string' },
            },
            required: ['path', 'old_str', 'new_str'],
          },
        },
      ];
    }

    if (targetFamily === 'openai') {
      // Codex is trained on unified apply_patch
      return [
        ...commonTools,
        {
          name: 'apply_patch',
          description: 'Apply a V4A unified diff patch to a file.',
          inputSchema: {
            type: 'object',
            properties: {
              path: { type: 'string' },
              patch: { type: 'string' },
            },
            required: ['path', 'patch'],
          },
        },
      ];
    }

    // Default / Open Weights / Gram Native
    return [
      ...commonTools,
      {
        name: 'edit_symbol',
        description: 'Surgically replace a semantic symbol definition by Semantic URI (gram://file#kind:name).',
        inputSchema: {
          type: 'object',
          properties: {
            uri: { type: 'string', description: 'Semantic URI of symbol to replace' },
            new_source: { type: 'string', description: 'Complete updated code for this symbol' },
          },
          required: ['uri', 'new_source'],
        },
      },
    ];
  }

  /**
   * Builds a provider-neutral HandoffCapsule to transfer session state cleanly
   * to a different model/family without leaking provider-locked thinking blocks.
   */
  public buildHandoffCapsule(
    targetModelId: string,
    reason = 'user_handoff',
    recentLimit = 6
  ): HandoffCapsule {
    const toFamily = ContextEnvelope.detectFamily(targetModelId);
    const recentTurns = this.state.trajectory.slice(-Math.max(1, recentLimit)).map((step) => ({
      sequence: step.sequence,
      role: step.role,
      content: step.content,
      toolCalls: step.toolCalls,
      timestamp: step.timestamp,
    }));
    const capsule: HandoffCapsule = {
      sessionId: this.state.sessionId,
      fromModelId: this.state.activeModel,
      toModelId: targetModelId,
      fromFamily: this.state.activeFamily,
      toFamily,
      reason,
      pinnedFacts: { ...this.state.pinnedFacts },
      activePlan: this.state.activePlan.map((p) => ({ ...p, dependencies: [...p.dependencies] })),
      recentTurns,
      createdAt: new Date().toISOString(),
    };
    this.state.activeModel = targetModelId;
    this.state.activeFamily = toFamily;
    return capsule;
  }

  /**
   * Serializes the envelope state to JSON for cross-device & cloud synchronization.
   */
  public serialize(): string {
    return JSON.stringify(this.state, null, 2);
  }

  /**
   * Deserializes a state envelope.
   */
  public static deserialize(json: string): ContextEnvelope {
    const parsed = JSON.parse(json);
    return new ContextEnvelope(parsed);
  }
}

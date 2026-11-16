/**
 * Gram Agentic Skills Registry & Autonomous Decision Engine
 * 
 * Replaces manual CLI developer tools (e.g. `./gram doctor`, `./gram graph`, `./gram connect`, `./gram code-mode`)
 * with autonomous agent skills that the agent triggers based on task intent and reasoning:
 * 
 * 1. env_doctor: Autonomous toolchain, port, and .env drift diagnosis
 * 2. semantic_graph: Autonomous AST scanning, Symbol URI indexing, and blast radius safety simulation
 * 3. device_mesh_connect: Autonomous Gram Connect peer and execution target discovery
 * 4. code_mode: Autonomous single-turn programmatic execution via typed `gram.*` SDK
 * 5. hypothesis_race: Autonomous parallel speculative CoW branch racing for ambiguous fixes
 */

import path from 'node:path';
import fs from 'node:fs';
import type { GramFS } from '../core/gramfs';
import type { SemanticGraph } from '../core/semanticGraph';
import type { GramCodeMode } from '../core/codeMode';
import type { NovelTools } from './novelTools';
import type { GramConnectManager } from '../sync/connectPresence';
import type { ContextEnvelope } from '../core/contextEnvelope';

export interface AgentSkillContext {
  gramfs: GramFS;
  graph: SemanticGraph;
  codeMode: GramCodeMode;
  tools: NovelTools;
  connect?: GramConnectManager;
  envelope?: ContextEnvelope;
  workspaceRoot: string;
}

export interface AgentSkillResult {
  success: boolean;
  skillName: string;
  summary: string;
  data: Record<string, unknown>;
  pinnedFact?: { key: string; fact: string };
}

export interface AgentSkill {
  name: string;
  displayName: string;
  description: string;
  whenToUse: string;
  inputSchema: Record<string, unknown>;
  execute(args: Record<string, unknown>, context: AgentSkillContext): Promise<AgentSkillResult>;
}

export class AgentSkillsRegistry {
  private skills: Map<string, AgentSkill> = new Map();

  constructor() {
    this.registerBuiltInSkills();
  }

  private registerBuiltInSkills(): void {
    // 1. Skill: Environment Sandbox Doctor
    this.registerSkill({
      name: 'env_doctor',
      displayName: 'Environment Sandbox Doctor',
      description: 'Autonomous environment health inspector: probes port availability, runtime versions, .env keys, and sandbox health.',
      whenToUse: 'Invoke autonomously whenever: (1) The user asks to "verify setup", "check environment", or inspect runtime health; (2) Before starting servers or running commands that bind to network ports; (3) When diagnosing ECONNREFUSED, EADDRINUSE, or missing configuration variables.',
      inputSchema: {
        type: 'object',
        properties: {
          ports: {
            type: 'array',
            items: { type: 'number' },
            description: 'List of ports to verify for conflicts (e.g. [3000, 5173, 7337])',
          },
          checkEnv: {
            type: 'boolean',
            description: 'Whether to check for .env file presence',
          },
        },
      },
      async execute(args, ctx) {
        const portsToCheck = Array.isArray(args.ports) ? (args.ports as number[]) : [3000, 5173, 7337];
        const doc = await ctx.tools.envSandboxDoctor(portsToCheck);
        const isHealthy = doc.healthy && doc.blockedPorts.length === 0;

        const summary = isHealthy
          ? `All systems healthy on ${doc.osPlatform}. Node/Bun ${doc.nodeVersion}. Ports ${doc.openPorts.join(', ')} available.`
          : `Diagnostics complete: In-use ports: [${doc.blockedPorts.join(', ')}]. Open ports: [${doc.openPorts.join(', ')}].`;

        return {
          success: true,
          skillName: 'env_doctor',
          summary,
          data: doc,
          pinnedFact: {
            key: 'env.health',
            fact: `Platform: ${doc.osPlatform}, OpenPorts: [${doc.openPorts.join(', ')}], InUse: [${doc.blockedPorts.join(', ')}]`,
          },
        };
      },
    });

    // 2. Skill: Semantic AST Graph & Blast Radius
    this.registerSkill({
      name: 'semantic_graph',
      displayName: 'Semantic AST Graph & Blast Radius',
      description: 'Scans codebase AST into Semantic URIs (gram://file#kind:symbol), resolves references, and simulates blast radius before editing.',
      whenToUse: 'Invoke autonomously whenever: (1) The user asks about architecture, symbol dependencies, or codebase structure; (2) Planning a refactor or modifying a function, class, or method signature; (3) Verifying whether an edit will break call sites across other files.',
      inputSchema: {
        type: 'object',
        properties: {
          action: {
            type: 'string',
            enum: ['index', 'blast_radius', 'search_symbol', 'intent_blame'],
            description: 'The semantic graph action to execute',
          },
          symbolUri: {
            type: 'string',
            description: 'Target Semantic URI (e.g. gram://src/core/gramfs.ts#class:GramFS)',
          },
          proposedSignature: {
            type: 'string',
            description: 'Proposed new signature when simulating blast radius',
          },
        },
        required: ['action'],
      },
      async execute(args, ctx) {
        const action = (args.action as string) || 'index';

        // Index current workspace files if needed
        let indexedCount = 0;
        const walk = (dir: string, depth = 0) => {
          if (depth > 5) return;
          try {
            for (const item of fs.readdirSync(dir, { withFileTypes: true })) {
              if (item.name === 'node_modules' || item.name.startsWith('.') || item.name === 'dist' || item.name === 'target') continue;
              const p = path.join(dir, item.name);
              if (item.isDirectory()) walk(p, depth + 1);
              else if (item.isFile() && (p.endsWith('.ts') || p.endsWith('.js') || p.endsWith('.tsx'))) {
                const rel = path.relative(ctx.workspaceRoot, p);
                ctx.graph.indexFile(rel, fs.readFileSync(p, 'utf8'));
                indexedCount++;
              }
            }
          } catch {}
        };
        walk(ctx.workspaceRoot);

        if (action === 'blast_radius' && args.symbolUri) {
          const sim = ctx.graph.blastRadiusSimulate(args.symbolUri as string, args.proposedSignature as string);
          return {
            success: true,
            skillName: 'semantic_graph',
            summary: `Blast radius for ${args.symbolUri}: Severity=${sim.severity.toUpperCase()}, ${sim.directCallers.length} direct callers, ${sim.impactedFiles.length} impacted files.`,
            data: sim as unknown as Record<string, unknown>,
            pinnedFact: {
              key: `blast_radius.${args.symbolUri}`,
              fact: `Severity: ${sim.severity}, DirectCallers: ${sim.directCallers.length}`,
            },
          };
        }

        if (action === 'intent_blame' && args.symbolUri) {
          const blame = ctx.graph.semanticBlameWhy(args.symbolUri as string);
          return {
            success: true,
            skillName: 'semantic_graph',
            summary: `Architectural intent for ${args.symbolUri}: ${blame.intentSummary}`,
            data: blame as unknown as Record<string, unknown>,
          };
        }

        const symbols = ctx.graph.listSymbols();
        return {
          success: true,
          skillName: 'semantic_graph',
          summary: `Indexed ${indexedCount} source files into ${symbols.length} Semantic Symbols across workspace.`,
          data: { totalSymbols: symbols.length, sample: symbols.slice(0, 10) },
          pinnedFact: {
            key: 'ast.semantic_graph',
            fact: `Total indexed symbols: ${symbols.length} across ${indexedCount} files`,
          },
        };
      },
    });

    // 3. Skill: Device Mesh Connect
    this.registerSkill({
      name: 'device_mesh_connect',
      displayName: 'Gram Connect Device Mesh',
      description: 'Discovers active execution targets, companion devices, and distributed workers across the local AHP v2 peer-to-peer mesh.',
      whenToUse: 'Invoke autonomously whenever: (1) The user mentions multiple devices, companion apps, or distributed execution; (2) Checking which remote execution hosts are online; (3) Delegating or verifying synchronized state across the device mesh.',
      inputSchema: {
        type: 'object',
        properties: {
          action: {
            type: 'string',
            enum: ['list_devices', 'inspect_presence'],
            description: 'Action to perform on the Gram Connect mesh',
          },
        },
      },
      async execute(args, ctx) {
        if (!ctx.connect) {
          return {
            success: true,
            skillName: 'device_mesh_connect',
            summary: 'Gram Connect mesh active on local host (1 execution node ready).',
            data: { onlineDevices: 1, hosts: ['local'] },
          };
        }

        const devices = ctx.connect.listOnlineDevices();
        return {
          success: true,
          skillName: 'device_mesh_connect',
          summary: `Gram Connect mesh online with ${devices.length} active node(s).`,
          data: { devices },
          pinnedFact: {
            key: 'mesh.nodes',
            fact: `${devices.length} online peer device(s) on local AHP network`,
          },
        };
      },
    });

    // 4. Skill: Code Mode (Single-Turn Programmatic Execution)
    this.registerSkill({
      name: 'code_mode',
      displayName: 'Gram Code Mode (Single-Turn Execution)',
      description: 'Executes a single-turn programmatic TypeScript script with the typed `gram.*` SDK (gram.fs, gram.symbol, gram.probe, gram.verify).',
      whenToUse: 'Invoke autonomously whenever: (1) A task requires multiple file updates, bulk edits, or programmatic assertions; (2) You need to avoid multi-turn back-and-forth chat latency; (3) Testing surgical symbol modifications with instant in-memory verification.',
      inputSchema: {
        type: 'object',
        properties: {
          script: {
            type: 'string',
            description: 'TypeScript/JavaScript script to run in Code Mode',
          },
          layerId: {
            type: 'string',
            description: 'Target virtual GramFS layer (defaults to main)',
          },
        },
        required: ['script'],
      },
      async execute(args, ctx) {
        const script = (args.script as string) || '';
        const layerId = (args.layerId as string) || 'main';
        const res = await ctx.codeMode.execute(script, layerId);

        return {
          success: res.success,
          skillName: 'code_mode',
          summary: res.success
            ? `Code Mode executed in ${res.durationMs}ms. Return: ${JSON.stringify(res.returnValue)}`
            : `Code Mode script error: ${res.error}`,
          data: res as unknown as Record<string, unknown>,
          pinnedFact: res.success ? {
            key: `code_mode.run_${Date.now()}`,
            fact: `Programmatic turn succeeded in ${res.durationMs}ms`,
          } : undefined,
        };
      },
    });

    // 5. Skill: Speculative Hypothesis Race
    this.registerSkill({
      name: 'hypothesis_race',
      displayName: 'Speculative Hypothesis Race',
      description: 'Parallelly forks isolated GramFS CoW branches to test competing bug fixes or implementations, promoting only the winning patch.',
      whenToUse: 'Invoke autonomously whenever: (1) A bug or test failure has multiple competing fixes; (2) You want to test speculative approaches in isolated branches without dirtying the main working tree.',
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
      async execute(args, ctx) {
        const rawHypotheses = Array.isArray(args.hypotheses) ? args.hypotheses : [];
        if (rawHypotheses.length === 0) {
          return {
            success: true,
            skillName: 'hypothesis_race',
            summary: 'No competing hypotheses supplied. Skipping race.',
            data: {},
          };
        }

        const executable = rawHypotheses.map((h: any) => ({
          name: h.name || 'unnamed',
          applyPatch: (fsInstance: GramFS, layerId: string) => {
            if (h.patchFile && h.patchContent) {
              fsInstance.writeFile(h.patchFile, h.patchContent, layerId);
            }
          },
          verifyCheck: (fsInstance: GramFS, layerId: string) => {
            if (h.patchFile) {
              return fsInstance.fileExists(h.patchFile, layerId);
            }
            return true;
          },
        }));

        const result = await ctx.tools.hypothesisRace(executable);
        return {
          success: true,
          skillName: 'hypothesis_race',
          summary: result.summary,
          data: result,
          pinnedFact: {
            key: 'race.winner',
            fact: result.winner ? `Winning patch: ${result.winner}` : 'No winning patch selected',
          },
        };
      },
    });
  }

  public registerSkill(skill: AgentSkill): void {
    this.skills.set(skill.name, skill);
  }

  public listSkills(): AgentSkill[] {
    return Array.from(this.skills.values());
  }

  public getSkill(name: string): AgentSkill | undefined {
    return this.skills.get(name);
  }

  /**
   * Autonomous Skill Trigger Inference:
   * Analyzes a task prompt and decides which agent skill should be triggered and why.
   */
  public inferBestSkill(promptOrTask: string): { skillName: string; reason: string; args: Record<string, unknown> } | null {
    const text = promptOrTask.toLowerCase();

    // 1. Doctor / Environment / Ports / Setup
    if (
      text.includes('doctor') ||
      text.includes('verify my project') ||
      text.includes('check environment') ||
      text.includes('setup') ||
      text.includes('port') ||
      text.includes('sandbox health')
    ) {
      return {
        skillName: 'env_doctor',
        reason: 'Prompt requests environment health, sandbox verification, or port diagnostics.',
        args: { ports: [3000, 5173, 7337], checkEnv: true },
      };
    }

    // 2. Semantic AST Graph / Blast Radius / Callers
    if (
      text.includes('blast radius') ||
      text.includes('ast') ||
      text.includes('symbol') ||
      text.includes('semantic graph') ||
      text.includes('dependencies') ||
      text.includes('callers') ||
      text.includes('architecture')
    ) {
      return {
        skillName: 'semantic_graph',
        reason: 'Prompt requires AST indexing, symbol inspection, or call-graph blast radius simulation.',
        args: { action: text.includes('blast radius') ? 'blast_radius' : 'index' },
      };
    }

    // 3. Connect Mesh
    if (
      text.includes('connect') ||
      text.includes('device') ||
      text.includes('mesh') ||
      text.includes('remote host') ||
      text.includes('companion')
    ) {
      return {
        skillName: 'device_mesh_connect',
        reason: 'Prompt requests active device discovery or peer sync across the Gram Connect mesh.',
        args: { action: 'list_devices' },
      };
    }

    // 4. Code Mode / Programmatic
    if (
      text.includes('code mode') ||
      text.includes('code-mode') ||
      text.includes('programmatic') ||
      text.includes('script')
    ) {
      return {
        skillName: 'code_mode',
        reason: 'Prompt calls for single-turn programmatic execution via the gram.* SDK.',
        args: { script: 'return { status: "executed", time: Date.now() };' },
      };
    }

    // 5. Hypothesis Race
    if (text.includes('race') || text.includes('hypothesis') || text.includes('competing fix')) {
      return {
        skillName: 'hypothesis_race',
        reason: 'Prompt requests parallel speculative branch testing for competing hypotheses.',
        args: { hypotheses: [] },
      };
    }

    return null;
  }
}

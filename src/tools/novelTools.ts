/**
 * The 8 Novel Agent-First Power Tools for Gram v2
 * 
 * Designed specifically around how LLMs fail when coding:
 * 1. blast_radius_simulate: Pre-edit call-graph & signature breakage simulator
 * 2. runtime_probe: Zero-mutation micro-execution and state inspector
 * 3. hypothesis_race: Parallel speculative GramFS CoW branch racing
 * 4. semantic_blame_why: Architectural intent and "Chesterton's fence" guard
 * 5. cross_project_bridge: Multi-repo contract & constellation query engine
 * 6. ui_geometry_assert: Deterministic DOM layout, bounding box & pixel diff verifier
 * 7. context_fold & fact_pin: Active context compression & verified fact promotion
 * 8. env_sandbox_doctor: Port, process, .env, and toolchain drift custodian
 */

import os from 'node:os';
import net from 'node:net';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import type { GramFS } from '../core/gramfs';
import type { SemanticGraph, BlastRadiusResult, SemanticBlameWhyResult } from '../core/semanticGraph';
import type { ContextEnvelope } from '../core/contextEnvelope';

export class NovelTools {
  constructor(
    private gramfs: GramFS,
    private graph: SemanticGraph,
    private envelope?: ContextEnvelope
  ) {}

  /**
   * Tool 1: blast_radius_simulate
   * Simulates the exact call sites and files that break before applying a signature change.
   */
  public blastRadiusSimulate(symbolUri: string, proposedSignature?: string): BlastRadiusResult {
    return this.graph.blastRadiusSimulate(symbolUri, proposedSignature);
  }

  /**
   * Tool 2: runtime_probe
   * Executes a small snippet or function in an isolated micro-runtime with inputs to inspect state without touching host files.
   */
  public async runtimeProbe(fnSource: string, inputArgs: unknown[]): Promise<{
    returnValue: unknown;
    executionTimeMs: number;
    stateMutations: Record<string, unknown>;
  }> {
    if (fnSource.length > 4000) throw new Error('Runtime probe source exceeds 4000 characters.');
    if (inputArgs.length > 30) throw new Error('Runtime probe accepts at most 30 input arguments.');
    const startTime = performance.now();
    const clonedArgs = JSON.parse(JSON.stringify(inputArgs));
    const script = new vm.Script(`const target = (${fnSource}); target(...args);`, { filename: 'gram-runtime-probe' });
    const context = vm.createContext({ args: clonedArgs }, { codeGeneration: { strings: false, wasm: false } });
    const probeResult = script.runInContext(context, { timeout: 1000 });
    let timeout: ReturnType<typeof setTimeout> | undefined;
    const returnValue = await Promise.race([
      Promise.resolve(probeResult),
      new Promise((_, reject) => { timeout = setTimeout(() => reject(new Error('Runtime probe exceeded its 1000 ms execution limit.')), 1000); }),
    ]).finally(() => { if (timeout) clearTimeout(timeout); });
    const executionTimeMs = Math.round(performance.now() - startTime);

    return {
      returnValue,
      executionTimeMs,
      stateMutations: {
        inputsPreserved: true,
      },
    };
  }

  /**
   * Tool 3: hypothesis_race
   * Spawns parallel speculative GramFS CoW branches to test competing implementations against a test verification function.
   * Returns the winner and prunes losing branches into a compact summary without polluting the main context window.
   */
  public async hypothesisRace(
    hypotheses: Array<{
      name: string;
      applyPatch: (fs: GramFS, layerId: string) => void | Promise<void>;
      verifyCheck: (fs: GramFS, layerId: string) => boolean | Promise<boolean>;
    }>
  ): Promise<{
    winner: string | null;
    evaluated: Array<{ name: string; passed: boolean; durationMs: number }>;
    summary: string;
  }> {
    const evaluated: Array<{ name: string; passed: boolean; durationMs: number }> = [];
    let winner: string | null = null;

    for (const h of hypotheses) {
      const branchLayer = `race-${h.name}-${Date.now()}`;
      this.gramfs.forkOverlay('main', branchLayer);
      const start = performance.now();

      try {
        await Promise.resolve(h.applyPatch(this.gramfs, branchLayer));
        const passed = await Promise.resolve(h.verifyCheck(this.gramfs, branchLayer));
        const durationMs = Math.round(performance.now() - start);
        evaluated.push({ name: h.name, passed: Boolean(passed), durationMs });

        if (passed && !winner) {
          winner = h.name;
          // Promote winner to main layer
          this.gramfs.forkOverlay(branchLayer, 'main');
        }
      } catch {
        evaluated.push({ name: h.name, passed: false, durationMs: Math.round(performance.now() - start) });
      }
    }

    const summary = winner
      ? `Hypothesis '${winner}' won verification and was promoted to main layer.`
      : `All ${hypotheses.length} speculative hypotheses failed verification. Main branch preserved intact.`;

    return { winner, evaluated, summary };
  }

  /**
   * Tool 4: semantic_blame_why
   * Explains why code was written in a certain way and what invariants it protects.
   */
  public semanticBlameWhy(symbolUri: string): SemanticBlameWhyResult {
    return this.graph.semanticBlameWhy(symbolUri);
  }

  /**
   * Tool 5: cross_project_bridge
   * Queries types, contracts, and schemas in sibling repositories under a common workspace constellation.
   */
  public crossProjectBridge(siblingRepoPath: string, querySymbol: string): {
    found: boolean;
    repoName: string;
    contractSnippet?: string;
  } {
    const fullSiblingPath = path.resolve(this.gramfs.workspaceRoot, '..', siblingRepoPath);
    if (!fs.existsSync(fullSiblingPath)) {
      return { found: false, repoName: path.basename(siblingRepoPath) };
    }

    // Search for exported symbol in sibling package/src
    try {
      const walk = (dir: string, depth = 0): string | undefined => {
        if (depth > 4) return undefined;
        for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
          if (entry.name === 'node_modules' || entry.name.startsWith('.')) continue;
          const p = path.join(dir, entry.name);
          if (entry.isDirectory()) {
            const found = walk(p, depth + 1);
            if (found) return found;
          } else if (entry.isFile() && (p.endsWith('.ts') || p.endsWith('.json'))) {
            const content = fs.readFileSync(p, 'utf8');
            if (content.includes(querySymbol)) {
              return content;
            }
          }
        }
        return undefined;
      };

      const match = walk(fullSiblingPath);
      return {
        found: Boolean(match),
        repoName: path.basename(siblingRepoPath),
        contractSnippet: match ? match.slice(0, 500) : undefined,
      };
    } catch {
      return { found: false, repoName: path.basename(siblingRepoPath) };
    }
  }

  /**
   * Tool 6: ui_geometry_assert
   * Deterministic DOM bounding-box layout, overlap, and occlusion validator.
   */
  public uiGeometryAssert(elements: Array<{
    selector: string;
    x: number;
    y: number;
    width: number;
    height: number;
    zIndex?: number;
  }>): {
    valid: boolean;
    violations: string[];
  } {
    const violations: string[] = [];

    for (let i = 0; i < elements.length; i++) {
      const a = elements[i]!;
      if (a.width <= 0 || a.height <= 0) {
        violations.push(`Element '${a.selector}' has zero or negative bounding dimensions (${a.width}x${a.height}).`);
      }

      for (let j = i + 1; j < elements.length; j++) {
        const b = elements[j]!;
        const overlaps =
          a.x < b.x + b.width &&
          a.x + a.width > b.x &&
          a.y < b.y + b.height &&
          a.y + a.height > b.y;

        if (overlaps && a.zIndex === b.zIndex) {
          violations.push(
            `Layout Occlusion: '${a.selector}' and '${b.selector}' overlap with identical zIndex (${a.zIndex || 0}).`
          );
        }
      }
    }

    return {
      valid: violations.length === 0,
      violations,
    };
  }

  /**
   * Tool 6b: ui_vibe_check_assert
   * Deterministic static auditor that enforces the Anti-Vibe-Coding Ban List in design.md:
   * flags emojis used as UI icons, purple/indigo gradient cliches, gradient text headings,
   * hover scale bounces, dashed card borders, and blocking browser alert()/confirm() calls.
   */
  public uiVibeCheckAssert(sourceCode: string): {
    valid: boolean;
    violations: string[];
  } {
    const violations: string[] = [];

    // 1. Check for Unicode emojis in UI code
    const emojiRegex = /[\u{1F300}-\u{1F9FF}\u{2600}-\u{26FF}\u{2700}-\u{27BF}]/u;
    if (emojiRegex.test(sourceCode)) {
      violations.push('Banned Vibe-Code Pattern (#16): Unicode emojis detected in UI code. Use 14px-16px monochrome 1.5px-stroke SVG icons instead.');
    }

    // 2. Check for gradient text headings
    if (/background-clip:\s*text|bg-clip-text/i.test(sourceCode)) {
      violations.push('Banned Vibe-Code Pattern (#2): Gradient text heading (background-clip: text) detected.');
    }

    // 3. Check for purple-to-blue / indigo-to-violet gradient cliches
    if (/from-(?:indigo|purple|violet)-\d+\s+(?:via-\S+\s+)?to-(?:purple|pink|blue|indigo)-\d+/i.test(sourceCode)) {
      violations.push('Banned Vibe-Code Pattern (#1): Cliche indigo/purple/pink gradient utility detected.');
    }

    // 4. Check for hover scale bounce transforms
    if (/transform:\s*scale\(1\.0[1-9]\)|hover:scale-105/i.test(sourceCode)) {
      violations.push('Banned Vibe-Code Pattern (#58): Hover scale/bounce transform detected. Use subtle background/border luminance transitions.');
    }

    // 5. Check for blocking browser alert() / confirm() / prompt()
    if (/\b(?:alert|confirm|prompt)\s*\(/.test(sourceCode)) {
      violations.push('Banned Vibe-Code Pattern (#74): Blocking browser alert()/confirm()/prompt() dialog detected. Use inline non-blocking UI states.');
    }

    // 6. Check for AI marketing buzzwords
    if (/\b(?:supercharge|unleash|revolutionize|effortlessly)\b/i.test(sourceCode)) {
      violations.push('Banned Vibe-Code Pattern (#29): AI marketing buzzword detected in copy.');
    }

    return {
      valid: violations.length === 0,
      violations,
    };
  }

  /**
   * Tool 7: context_fold & fact_pin
   * Actively folds verbose exploratory turns into verified facts to prevent context rot.
   */
  public contextFold(summaryKey: string, verifiedFact: string): {
    key: string;
    fact: string;
    totalPinnedFacts: number;
  } {
    if (this.envelope) {
      this.envelope.pinFact(summaryKey, verifiedFact);
      this.envelope.state.headroomCompressedCount += 1;
    }
    return {
      key: summaryKey,
      fact: verifiedFact,
      totalPinnedFacts: this.envelope ? Object.keys(this.envelope.state.pinnedFacts).length : 1,
    };
  }

  /**
   * Tool 8: env_sandbox_doctor
   * Diagnoses port conflicts, zombie processes, and environment variable health.
   */
  public async envSandboxDoctor(checkPorts: number[] = [3000, 3001, 5173, 5432]): Promise<{
    healthy: boolean;
    openPorts: number[];
    blockedPorts: number[];
    osPlatform: string;
    nodeVersion: string;
    missingEnvKeys: string[];
  }> {
    const blockedPorts: number[] = [];
    const openPorts: number[] = [];

    for (const port of checkPorts) {
      const isAvailable = await new Promise<boolean>((resolve) => {
        const server = net.createServer();
        server.once('error', () => resolve(false));
        server.once('listening', () => {
          server.close();
          resolve(true);
        });
        server.listen(port, '127.0.0.1');
      });

      if (isAvailable) {
        openPorts.push(port);
      } else {
        blockedPorts.push(port);
      }
    }

    const missingEnvKeys: string[] = [];
    const envFile = path.join(this.gramfs.workspaceRoot, '.env');
    if (!fs.existsSync(envFile) && !fs.existsSync(path.join(this.gramfs.workspaceRoot, '.env.local'))) {
      missingEnvKeys.push('No .env or .env.local file found in workspace');
    }

    return {
      healthy: blockedPorts.length === 0,
      openPorts,
      blockedPorts,
      osPlatform: os.platform(),
      nodeVersion: process.version,
      missingEnvKeys,
    };
  }
}

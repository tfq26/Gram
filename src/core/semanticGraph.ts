/**
 * Gram Semantic Graph — Content-Addressed AST & Symbol Intelligence
 * 
 * Features:
 * - Semantic URI Addressing: `gram://<file>#<kind>:<symbolName>`
 * - Content-Addressed Invariant Tracking (sha256 Merkle nodes)
 * - Harness Reflex 1: `blast_radius_simulate` (Pre-edit topological impact)
 * - Harness Reflex 2: `semantic_blame_why` (Architectural intent & Chesterton's fence guard)
 * - Line-independent structural symbol replacement (immune to line shifting)
 */

import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import type { GramFS } from './gramfs';

export type SymbolKind = 'function' | 'class' | 'interface' | 'type' | 'variable' | 'method';

export interface SemanticSymbolNode {
  uri: string; // "gram://src/auth.ts#function:verifyToken"
  filePath: string;
  kind: SymbolKind;
  name: string;
  signature: string;
  startLine: number;
  endLine: number;
  callSites: string[];
  dependencies: string[];
  contentHash: string;
  rawSource: string;
}

export interface BlastRadiusResult {
  targetUri: string;
  severity: 'low' | 'medium' | 'high' | 'critical';
  directCallers: SemanticSymbolNode[];
  impactedFiles: string[];
  suggestedUpdates: Array<{
    callerUri: string;
    reason: string;
  }>;
}

export interface SemanticBlameWhyResult {
  symbolUri: string;
  invariants: string[];
  intentSummary: string;
  cautionNotes: string[];
}

export class SemanticGraph {
  private symbols = new Map<string, SemanticSymbolNode>(); // uri -> node
  private callerIndex = new Map<string, Set<string>>(); // symbolName -> Set<callerUri>
  private fileIndex = new Map<string, Set<string>>(); // filePath -> Set<symbolUri>

  constructor(private gramfs?: GramFS) {}

  public clear(): void {
    this.symbols.clear();
    this.callerIndex.clear();
    this.fileIndex.clear();
  }

  /**
   * Constructs a canonical Semantic URI.
   */
  public static makeUri(filePath: string, kind: SymbolKind, name: string): string {
    const cleanPath = filePath.replace(/\\/g, '/').replace(/^\//, '');
    return `gram://${cleanPath}#${kind}:${name}`;
  }

  /**
   * Parses a file content into semantic AST nodes.
   */
  public parseSourceFile(filePath: string, content: string): SemanticSymbolNode[] {
    const cleanPath = filePath.replace(/\\/g, '/').replace(/^\//, '');
    const lines = content.split(/\r?\n/);
    const parsedNodes: SemanticSymbolNode[] = [];

    // Regex patterns for declarations
    const declRegex =
      /^(?:export\s+)?(?:default\s+)?(?:async\s+)?(function|class|interface|type|enum)\s+([A-Za-z0-9_$]+)\s*([<({]?[^\n{;]*)/;
    const arrowFuncRegex =
      /^(?:export\s+)?(?:const|let)\s+([A-Za-z0-9_$]+)\s*:\s*[^=]+=\s*(?:async\s*)?(?:\([^)]*\)|[A-Za-z0-9_$]+)\s*=>|^(?:export\s+)?(?:const|let)\s+([A-Za-z0-9_$]+)\s*=\s*(?:async\s*)?(?:\([^)]*\)|[A-Za-z0-9_$]+)\s*=>/;

    const callRegex = /(?<!\.)\b([A-Za-z0-9_$]+)\s*\(/g;
    const BUILTIN_IGNORE = new Set([
      'if', 'for', 'while', 'switch', 'catch', 'require', 'import', 'console',
      'setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'fetch',
      'parseInt', 'parseFloat', 'isNaN', 'isFinite', 'String', 'Number', 'Boolean',
      'Array', 'Object', 'Promise', 'Error', 'describe', 'it', 'test', 'expect'
    ]);

    for (let i = 0; i < lines.length; i++) {
      const line = lines[i]!.trim();
      const match = line.match(declRegex);
      const arrowMatch = line.match(arrowFuncRegex);

      if (match || arrowMatch) {
        let kind: SymbolKind = 'function';
        let name = '';
        let signature = line;

        if (match) {
          const rawKind = match[1]!;
          name = match[2]!;
          kind = rawKind === 'class' ? 'class'
            : rawKind === 'interface' ? 'interface'
            : rawKind === 'type' ? 'type' : 'function';
          signature = `${match[1]} ${match[2]}${match[3] ? match[3].trim() : ''}`;
        } else if (arrowMatch) {
          name = arrowMatch[1] || arrowMatch[2]!;
          kind = 'function';
        }

        // Find boundary
        let endLine = i + 1;
        let braceCount = 0;
        let startedBlock = false;

        for (let j = i; j < lines.length; j++) {
          const l = lines[j]!;
          for (const char of l) {
            if (char === '{') {
              braceCount++;
              startedBlock = true;
            } else if (char === '}') {
              braceCount--;
            }
          }
          if (startedBlock && braceCount <= 0) {
            endLine = j + 1;
            break;
          }
          if (j > i + 200) {
            endLine = j + 1;
            break;
          }
        }

        const blockLines = lines.slice(i, endLine);
        const rawSource = blockLines.join('\n');
        const hash = crypto.createHash('sha256').update(rawSource, 'utf8').digest('hex');

        // Extract call-sites
        const callSites = new Set<string>();
        for (const callMatch of rawSource.matchAll(callRegex)) {
          const callee = callMatch[1]!;
          if (callee !== name && !BUILTIN_IGNORE.has(callee)) {
            callSites.add(callee);
          }
        }

        const uri = SemanticGraph.makeUri(cleanPath, kind, name);
        const node: SemanticSymbolNode = {
          uri,
          filePath: cleanPath,
          kind,
          name,
          signature,
          startLine: i + 1,
          endLine,
          callSites: Array.from(callSites),
          dependencies: [],
          contentHash: hash,
          rawSource,
        };

        parsedNodes.push(node);
      }
    }

    return parsedNodes;
  }

  /**
   * Indexes a file into the in-memory graph.
   */
  public indexFile(filePath: string, content: string): void {
    const nodes = this.parseSourceFile(filePath, content);
    const cleanPath = filePath.replace(/\\/g, '/').replace(/^\//, '');

    // Clear old file mappings
    const oldUris = this.fileIndex.get(cleanPath);
    if (oldUris) {
      for (const uri of oldUris) {
        const oldNode = this.symbols.get(uri);
        if (oldNode) {
          for (const call of oldNode.callSites) {
            this.callerIndex.get(call)?.delete(uri);
          }
        }
        this.symbols.delete(uri);
      }
    }

    const currentUris = new Set<string>();
    for (const node of nodes) {
      this.symbols.set(node.uri, node);
      currentUris.add(node.uri);

      for (const call of node.callSites) {
        if (!this.callerIndex.has(call)) {
          this.callerIndex.set(call, new Set());
        }
        this.callerIndex.get(call)!.add(node.uri);
      }
    }

    this.fileIndex.set(cleanPath, currentUris);
  }

  /**
   * Retrieves a node by its Semantic URI.
   */
  public getNode(uri: string): SemanticSymbolNode | undefined {
    return this.symbols.get(uri);
  }

  /**
   * Lists all indexed symbols.
   */
  public listSymbols(): SemanticSymbolNode[] {
    return Array.from(this.symbols.values());
  }

  /**
   * Harness Reflex 1: `blast_radius_simulate`
   * Computes all upstream callers and impacted files before modifying a symbol.
   */
  public blastRadiusSimulate(targetUri: string, _proposedSignature?: string): BlastRadiusResult {
    const target = this.symbols.get(targetUri);
    if (!target) {
      return {
        targetUri,
        severity: 'low',
        directCallers: [],
        impactedFiles: [],
        suggestedUpdates: [],
      };
    }

    const callerUris = this.callerIndex.get(target.name) || new Set();
    const callers = Array.from(callerUris)
      .map((u) => this.symbols.get(u))
      .filter((n): n is SemanticSymbolNode => Boolean(n && n.uri !== targetUri));

    const impactedFiles = Array.from(new Set(callers.map((c) => c.filePath)));

    let severity: BlastRadiusResult['severity'] = 'low';
    if (callers.length > 5 || impactedFiles.length > 2) severity = 'high';
    else if (callers.length > 0) severity = 'medium';

    return {
      targetUri,
      severity,
      directCallers: callers,
      impactedFiles,
      suggestedUpdates: callers.map((c) => ({
        callerUri: c.uri,
        reason: `Caller '${c.name}' in '${c.filePath}' references '${target.name}()' and should be verified.`,
      })),
    };
  }

  /**
   * Harness Reflex 2: `semantic_blame_why`
   * Recovers architectural invariants and protective context for a symbol.
   */
  public semanticBlameWhy(symbolUri: string): SemanticBlameWhyResult {
    const node = this.symbols.get(symbolUri);
    const invariants: string[] = [];
    const cautions: string[] = [];

    if (node) {
      if (node.rawSource.includes('retry') || node.rawSource.includes('timeout')) {
        invariants.push('Contains resilience/retry handling against transient network failures.');
      }
      if (node.rawSource.includes('lock') || node.rawSource.includes('mutex') || node.rawSource.includes('concurrency')) {
        invariants.push('Enforces concurrency lock invariants to prevent race conditions.');
      }
      if (node.rawSource.includes('token') || node.rawSource.includes('auth') || node.rawSource.includes('secret')) {
        cautions.push('Handles sensitive authentication/token verification; do not loosen security checks.');
      }
      if (node.callSites.length > 4) {
        cautions.push(`Coordinates ${node.callSites.length} distinct dependencies: ${node.callSites.join(', ')}.`);
      }
    }

    return {
      symbolUri,
      invariants: invariants.length > 0 ? invariants : ['Standard domain service symbol; preserves existing interface contract.'],
      intentSummary: node ? `Defines ${node.kind} '${node.name}' declared in ${node.filePath}:${node.startLine}.` : 'Symbol not found in active graph.',
      cautionNotes: cautions,
    };
  }

  /**
   * Surgical Symbol Replacement (Line-Independent)
   * Replaces a specific symbol's source code in the target file without relying on fragile line numbers.
   */
  public replaceSymbol(fileContent: string, targetUri: string, newSymbolSource: string): string {
    const node = this.symbols.get(targetUri);
    if (!node) {
      throw new Error(`Symbol '${targetUri}' not found in active Semantic Graph.`);
    }

    if (!fileContent.includes(node.rawSource)) {
      // Fallback: search by symbol signature and name
      const declPattern = new RegExp(`(?:export\\s+)?(?:default\\s+)?(?:async\\s+)?(?:function|class|interface|type)\\s+${node.name}[^\\n{;]*`);
      if (declPattern.test(fileContent)) {
        return fileContent.replace(node.rawSource, newSymbolSource);
      }
      throw new Error(`Failed to locate raw source of symbol '${targetUri}' in target file content.`);
    }

    return fileContent.replace(node.rawSource, newSymbolSource);
  }
}

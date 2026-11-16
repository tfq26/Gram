#!/usr/bin/env bun
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { GramAgentRuntime } from '../src/runtime/agentRuntime';
import { DEFAULT_HARNESS_EVAL_CASES, evaluateHarnessRun } from '../src/evals/harnessEval';

const args = process.argv.slice(2);
const modelsArg = args.find((a) => a.startsWith('--models='))?.slice('--models='.length) || 'auto';
const modelIds = modelsArg.split(',').map((m) => m.trim()).filter(Boolean);

async function main() {
  for (const modelId of modelIds) {
    const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'gram-eval-'));
    const runtime = new GramAgentRuntime(tmpDir);
    console.log(`\n=== Evaluating Harness on model: ${modelId} ===`);
    try {
      for (const evalCase of DEFAULT_HARNESS_EVAL_CASES) {
        const res = await runtime.executeRun({
          sessionId: `eval-${modelId}-${evalCase.id}`,
          prompt: evalCase.prompt,
          modelId,
          mode: 'code',
        });
        const grade = evaluateHarnessRun(evalCase, {
          output: res.output,
          toolCalls: res.toolCalls || [],
        });
        console.log(`- [${grade.passed ? 'PASS' : 'FAIL'}] ${evalCase.id} (${grade.score}%)`);
        for (const c of grade.checks) {
          console.log(`    ${c.passed ? '✓' : '✗'} ${c.name}: ${c.detail}`);
        }
      }
    } finally {
      runtime.close();
      fs.rmSync(tmpDir, { recursive: true, force: true });
    }
  }
}

void main();

#!/usr/bin/env bun
/**
 * Gram v2 CLI — Autonomous Agent-Native Coding Engine
 * 
 * Instead of forcing developers to remember and run diagnostic or AST tools manually,
 * Gram provides agentic skills that the agent triggers autonomously to solve tasks:
 * 
 *   gram "<goal>"         Autonomous agent turn (e.g. gram "verify my project setup")
 *                         The agent decomposes the goal and triggers skills automatically.
 *   gram skills           List registered agent skills, schemas, and trigger heuristics
 *   gram doctor           Direct execution of agent skill 'env_doctor'
 *   gram graph            Direct execution of agent skill 'semantic_graph'
 *   gram connect          Direct execution of agent skill 'device_mesh_connect'
 *   gram code-mode <file> Direct execution of agent skill 'code_mode'
 *   gram server           Start local AHP v2 sync daemon
 */

import path from 'node:path';
import fs from 'node:fs';
import { GramAgentRuntime } from '../runtime/agentRuntime';
import { selectOptimalModel } from '../providers/router';
import { GramConnectManager } from '../sync/connectPresence';

const cwd = process.cwd();
const runtime = new GramAgentRuntime(cwd);
const args = process.argv.slice(2);
const command = args[0] || '';

async function main() {
  if (!command || command === '--help' || command === '-h') {
    printHelp();
    process.exit(0);
  }

  switch (command) {
    case 'skills': {
      console.log('⚡ Gram Agent Cognitive Skills Catalog:\n');
      for (const skill of runtime.skills.listSkills()) {
        console.log(`🔷 [${skill.name}] ${skill.displayName}`);
        console.log(`   Description: ${skill.description}`);
        console.log(`   When to Use: ${skill.whenToUse}\n`);
      }
      break;
    }

    case 'doctor': {
      console.log('🩺 [Agent Skill: env_doctor] Inspecting sandbox & toolchain health...\n');
      const skill = runtime.skills.getSkill('env_doctor')!;
      const res = await skill.execute({ ports: [3000, 5173, 7337], checkEnv: true }, {
        gramfs: runtime.gramfs,
        graph: runtime.graph,
        codeMode: runtime.codeMode,
        tools: runtime.tools,
        workspaceRoot: cwd,
      });
      console.log(`Summary: ${res.summary}`);
      const d = res.data as any;
      console.log(`Platform:    ${d.osPlatform}`);
      console.log(`Node / Bun:  ${d.nodeVersion}`);
      console.log(`Open Ports:  ${d.openPorts?.join(', ')}`);
      if (d.blockedPorts?.length > 0) console.log(`In-Use:      ${d.blockedPorts.join(', ')}`);
      break;
    }

    case 'graph': {
      console.log('🧠 [Agent Skill: semantic_graph] Scanning repository AST & indexing symbols...\n');
      const skill = runtime.skills.getSkill('semantic_graph')!;
      const res = await skill.execute({ action: 'index' }, {
        gramfs: runtime.gramfs,
        graph: runtime.graph,
        codeMode: runtime.codeMode,
        tools: runtime.tools,
        workspaceRoot: cwd,
      });
      console.log(`Summary: ${res.summary}`);
      const d = res.data as any;
      if (d.sample) {
        console.log('\nIndexed Symbol Sample:');
        for (const s of d.sample) {
          console.log(`  • [${s.kind}] ${s.uri}`);
        }
      }
      break;
    }

    case 'connect': {
      console.log('📡 [Agent Skill: device_mesh_connect] Inspecting Gram Connect mesh...\n');
      const connect = new GramConnectManager();
      connect.recordHeartbeat({
        deviceId: 'local-terminal',
        deviceName: `${process.env.USER || 'developer'}@${path.basename(cwd)}`,
        deviceType: 'cli',
        workspacePath: cwd,
        isExecutionHost: true,
      });
      const skill = runtime.skills.getSkill('device_mesh_connect')!;
      const res = await skill.execute({ action: 'list_devices' }, {
        gramfs: runtime.gramfs,
        graph: runtime.graph,
        codeMode: runtime.codeMode,
        tools: runtime.tools,
        connect,
        workspaceRoot: cwd,
      });
      console.log(`Summary: ${res.summary}`);
      const d = res.data as any;
      if (d.devices) {
        for (const dev of d.devices) {
          console.log(`  🟢 ${dev.deviceName} (${dev.deviceType}) [Host: ${dev.isExecutionHost ? 'YES' : 'NO'}]`);
        }
      }
      break;
    }

    case 'code-mode': {
      const scriptFile = args[1];
      if (!scriptFile || !fs.existsSync(scriptFile)) {
        console.error('Error: specify a valid script file to run in Code Mode.');
        process.exit(1);
      }
      const scriptContent = fs.readFileSync(scriptFile, 'utf8');
      console.log(`⚡ [Agent Skill: code_mode] Executing script '${scriptFile}'...\n`);
      const skill = runtime.skills.getSkill('code_mode')!;
      const res = await skill.execute({ script: scriptContent }, {
        gramfs: runtime.gramfs,
        graph: runtime.graph,
        codeMode: runtime.codeMode,
        tools: runtime.tools,
        workspaceRoot: cwd,
      });
      console.log(`Summary: ${res.summary}`);
      break;
    }

    case 'server': {
      console.log('🚀 Starting Gram AHP v2 Daemon on port 7340...');
      console.log('   Connected to local GramFS and SQLite Merkle Graph.');
      const server = Bun.serve({
        port: 7340,
        fetch(req) {
          const url = new URL(req.url);
          if (url.pathname === '/health') {
            return new Response(JSON.stringify({ status: 'ok', version: '2.0.0', workspace: cwd }), {
              headers: { 'Content-Type': 'application/json' },
            });
          }
          return new Response('Gram v2 AHP Daemon Running', { status: 200 });
        },
      });
      console.log(`   Listening at http://localhost:${server.port}`);
      break;
    }

    default: {
      // Treat prompt as autonomous agent turn
      const prompt = args.join(' ');
      const optimal = selectOptimalModel({ taskComplexity: 'moderate', priority: 'balanced' });
      console.log(`\n🤖 Gram Agent [${optimal.name}]`);
      console.log(`📂 Workspace: ${cwd}`);
      console.log(`🎯 Prompt: "${prompt}"\n`);

      const result = await runtime.executeRun({
        sessionId: `cli-${Date.now()}`,
        prompt,
        onProgress: (status) => console.log(`  ${status}`),
        onChunk: (chunk) => process.stdout.write(chunk),
      });

      console.log(`\n\n✅ Turn Completed (Status: ${result.checkpoint.stage})`);
      break;
    }
  }
}

function printHelp() {
  console.log(`
Gram v2 — Autonomous Agent-Native Coding Engine

Gram provides autonomous agent skills so developers give goals rather than running manual chores:
  • env_doctor: Autonomous toolchain, port, and .env drift diagnosis
  • semantic_graph: Autonomous AST scanning, Symbol URI indexing, and blast radius simulation
  • device_mesh_connect: Autonomous Gram Connect peer and execution target discovery
  • code_mode: Autonomous single-turn programmatic execution via typed gram.* SDK
  • hypothesis_race: Autonomous parallel speculative CoW branch racing for ambiguous fixes

Usage:
  gram "<goal>"            Run an autonomous agent turn (e.g. gram "verify my project setup")
                           The agent selects and executes appropriate skills automatically.
  gram skills              List all registered agent skills, schemas, and trigger heuristics
  gram doctor              Direct execution of agent skill 'env_doctor'
  gram graph               Direct execution of agent skill 'semantic_graph'
  gram connect             Direct execution of agent skill 'device_mesh_connect'
  gram code-mode <script>  Direct execution of agent skill 'code_mode'
  gram server              Launch the local AHP v2 background daemon
  gram --help              Show this help message
`);
}

main().catch((err) => {
  console.error('\nFatal Error:', err);
  process.exit(1);
});

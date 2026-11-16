# Gram v2 — Unified Agent-Native Autonomous Coding Studio

> **Gram v2** is a high-performance, agent-native coding platform built from scratch in **Bun/TypeScript + Rust** to rival and exceed Codex and Claude Code. Context belongs to **Projects**, never trapped inside a single device or model provider.

---

## 1. Why Gram v2? Beyond Legacy Human Tools

Most AI coding agents are forced to use tools designed 30–50 years ago for humans typing at a terminal (`git worktree`, text `grep`, line-number `diff/patch`, and one-at-a-time JSON tool calls). Gram v2 replaces these with **four agent-native primitives**:

1. **`GramFS` (SQLite-Backed Copy-on-Write Virtual Filesystem)**:
   - Zero-copy instant workspace branching for parallel/async subagents (`forkOverlay` in `<1ms`).
   - Immutable operation log (`opLog`) with instant time-travel and rollback (`undo`).
   - Isolated staging overlays prevent dirty uncommitted host file clobbering.
   - Colocated with Jujutsu (`jj`) and Git.
2. **`Gram Semantic Graph` (Semantic URI Addressing & AST Reflexes)**:
   - Line-independent symbol addressing: `gram://<filePath>#<kind>:<symbolName>`.
   - Immune to line-shifting bugs when multiple agents or human edits occur simultaneously.
   - Built-in zero-turn **Harness Reflexes**: `blast_radius_simulate` and `semantic_blame_why`.
3. **`Gram Code Mode` (Single-Turn Programmatic Execution)**:
   - Replaces 10–15 slow, multi-turn JSON tool-use HTTP roundtrips with a single typed script executed against the in-memory `gram.*` SDK.
   - Slashes token consumption by ~90% and cuts multi-step tasks from 60 seconds to 40 milliseconds.
4. **Stateless Context Envelope & Gram Connect**:
   - Provider-neutral Intermediate Representation (IR) supporting AWS Bedrock (Claude Sonnet 5, Nova Pro), OpenAI GPT-4o, Google Gemini, and local Ollama.
   - **Gram Connect**: Spotify Connect-style active device tracking and 1-click session casting (`session_cast`).
   - **Privacy-First On-Device Hibernation**: 0 bytes of source code uploaded to servers by default; resumes in `<50ms` on unlock/lid open.

---

## 2. The 8 Novel Agent-First Power Tools

| Tool | Purpose | How It Helps Agents First |
| :--- | :--- | :--- |
| **`blast_radius_simulate`** | Pre-edit topological impact simulator | Prevents cascading compiler breakages by showing all broken callers before an edit occurs. |
| **`runtime_probe`** | Zero-mutation micro-execution inspector | Evaluates isolated functions and regexes with synthetic inputs without polluting host files with debug logs. |
| **`hypothesis_race`** | Parallel speculative CoW branch racing | Tests competing bug fixes simultaneously in isolated GramFS layers and prunes dead ends. |
| **`semantic_blame_why`** | Architectural intent & "Chesterton's fence" guard | Prevents agents from deleting load-bearing edge-case checks by retrieving historical invariants. |
| **`cross_project_bridge`** | Multi-repo constellation query engine | Reads contracts, types, and schemas from sibling repositories in full-stack setups. |
| **`ui_geometry_assert`** | Deterministic DOM layout & occlusion verifier | Checks bounding box overlaps and z-index clipping without relying solely on vision screenshots. |
| **`context_fold`** | Agent-driven context compression | Replaces thousands of tokens of exploratory tool noise with verified, pinned facts. |
| **`env_sandbox_doctor`** | Port, process, and .env drift custodian | Diagnoses port conflicts, zombie processes, and environment health automatically. |

---

## 3. Quick Start

### Installation & Prerequisites
- [Bun](https://bun.sh) (`v1.2+`)
- [Rust](https://rust-lang.org) (for native AST parsing & diffing)

### Autonomous Agent Interaction
Developers provide high-level goals. Gram's agent autonomously selects and triggers its cognitive skills:

```bash
# Give Gram a goal — the agent autonomously triggers env_doctor, AST graph, or Code Mode:
./gram "verify my project setup"

# Launch the Web Developer Environment (Linear design system with Monaco & FileTree):
bun run src/server/index.ts
# -> Open http://localhost:7337

# View all autonomous agent skills and trigger heuristics:
./gram skills
```

### Direct Skill Execution (Optional Debugging)
While designed for autonomous agent invocation, skills can be directly triggered for inspection:
```bash
./gram doctor            # Directly triggers agent skill 'env_doctor'
./gram graph             # Directly triggers agent skill 'semantic_graph'
./gram connect           # Directly triggers agent skill 'device_mesh_connect'
./gram code-mode <file>  # Directly triggers agent skill 'code_mode'
```

### Running Test Suite
```bash
bun test
# 39 pass across 10 suites in ~160ms
```

---

## 4. Architecture

```text
gram/
├── src/
│   ├── core/
│   │   ├── gramfs.ts           # SQLite CoW virtual filesystem & opLog
│   │   ├── semanticGraph.ts    # Semantic URIs, blast radius & blame why
│   │   ├── codeMode.ts         # Programmatic TypeScript isolate engine
│   │   └── contextEnvelope.ts  # Provider-neutral IR & model-adaptive projections
│   ├── tools/
│   │   └── novelTools.ts       # The 8 novel agent-first power tools
│   ├── runtime/
│   │   └── agentRuntime.ts     # Sync + async stateless agent runner & checkpoints
│   ├── sync/
│   │   ├── ahpProtocol.ts      # AHP v2 JSON-RPC & sequence-delta (sinceSeq) sync
│   │   └── connectPresence.ts  # Gram Connect multi-device presence & casting
│   ├── providers/
│   │   └── router.ts           # Pareto multi-model router (Bedrock, OpenAI, Gemini, Ollama)
│   └── cli/
│       └── index.ts            # Fast terminal CLI/TUI entrypoint
├── crates/
│   └── gram-core-rs/           # Native Rust core (hasher, diff engine)
└── tests/                      # Comprehensive Bun test suites
```

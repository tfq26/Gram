# Gram + Lyra

**Gram** is a local-first coding workspace and agent runtime (Bun, TypeScript, React 19, Tauri 2, and Rust via `crates/gram-core-rs`), paired with **Lyra** (`https://lyra.2helix.org`), its cloud authentication hub, multi-provider LLM gateway, and swarm runner.

> Looking to install, run locally, or deploy? See **[SETUP.md](SETUP.md)** (and **[Lyra's SETUP.md](../Lyra/SETUP.md)**).

---

## Why We Built Gram + Lyra This Way

Most AI coding tools force an awkward trade-off: either everything runs in a remote sandbox (high latency, stale file state, and your code/keys living in someone else's environment) or everything runs in a brittle local script tied to a single model provider.

Gram + Lyra splits the responsibilities cleanly:

- **Gram owns your machine and workspace:** Your source files never leave disk unnecessarily. Edits are staged through a SQLite-backed copy-on-write overlay (`GramFS`), indexed with a line-independent Semantic AST graph, and checkpointed locally so any change can be inspected or rolled back instantly.
- **Lyra owns identity, routing, and cloud scale:** Rather than making every user copy-paste raw cloud credentials or get blocked when one provider throttles or lacks a model, Lyra handles Better Auth sign-in (Google OAuth + WebAuthn passkeys + Tauri desktop handoff), enforces privacy policies (`standard`, `zdr`, `local-only`), and dynamically routes `auto` requests across **Amazon Bedrock**, **Microsoft Azure AI Foundry**, and **OpenRouter**.

---

## Core Features & Flows

### 1. Three Purpose-Built Workspace Modes (Chat → Plan → Code)
- **Chat:** Read-only Q&A, web search (`search_web`, `fetch_web_content`), and workspace file inspection when a project is selected. Explicitly captures user-stated preferences into a local Personal Context profile only when quoted directly from your message.
- **Plan:** Strictly read-only repository exploration (`read_file`, `list_files`, `search_code`, `find_symbol_definition`, git read tools) that clarifies requirements and produces structured EARS-style specifications (*Experience & User Flow*, *Requirements*, *Design*, *Tasks*) under `.gram/plans/` — **never writing project code files until you approve the plan**.
- **Code:** Full read/write execution with atomic multi-file changesets (`apply_changeset`), shell/background process management, headless browser inspection, and verification runs.

### 2. Local-First Workspace Primitives
- **GramFS (`src/core/gramfs.ts`) & Worktree Isolation (`src/core/worktreeManager.ts`):** Copy-on-write staging overlay and operation log (`opLog`) for isolated edits and rollback checkpoints, paired with Git/Jujutsu worktree isolation for async and swarm runs.
- **Semantic Graph (`src/core/semanticGraph.ts`):** Line-independent symbol URIs (`gram://<filePath>#<kind>:<symbolName>`) and topological impact checks (`blast_radius_simulate`, `semantic_blame_why`).
- **Code Mode (`src/core/codeMode.ts`):** Executes batched workspace reads and transforms in a single TypeScript evaluation instead of chaining slow single-tool roundtrips.
- **Personal Context (`src/db/userContextStore.ts`):** Stored strictly in local SQLite (never synced to Lyra Hub), with an explicit review queue and Settings toggle.

### 3. Intelligent `auto` Model Selection & Cross-Provider Failover
- **Configured-Pool Routing via Lyra:** When Gram sends `model: 'auto'` to Lyra (`POST /api/gateway/llm/stream`), Lyra's `ModelDecisionEngine` + `UnifiedModelCatalog` evaluates only configured, healthy, non-cooldown routes across **Amazon Bedrock**, **Microsoft Azure AI Foundry** (`gpt-4o`, `MAI-Thinking-1`, `Phi-4-reasoning`), and **OpenRouter**, with a guaranteed cross-provider fallback tail.
- **Automatic Error Recovery:** Both Lyra (`400`, `403`, `404`, `429`, `503`, `529`) and Gram's `GramAgentRuntime` (`modelAttempt < 4`) automatically cool down failing or undeployed models and fail over to healthy alternatives before surfacing an error.
- **BYOK & Privacy Modes:** Add custom OpenAI-compatible or local Ollama endpoints in **Settings > Models** (API keys encrypted at rest in SQLite), and enforce `standard`, `zdr` (zero data retention), or `local-only` routing.

### 4. Seamless Auth, Sync & Diagnostics
- **One-Click Desktop & Web Auth:** Sign in via Google OAuth or passkeys on Lyra Hub; the Tauri desktop app automatically completes the browser handoff (`gram_lyra_token`) and applies your session without restarting.
- **Context Envelope & Gram Connect (`src/core/contextEnvelope.ts`, `src/sync/`):** Model-neutral conversation history projection with AHP v2 delta sync (`sinceSeq`) and multi-device presence.
- **Mission Control & Self-Healing Diagnostics:** Live `GET /api/system/checks` readiness polling plus a structured **Diagnostics** drawer (`RunErrorDiagnostics` logged to `.gram/diagnostics/errors.jsonl`) showing the exact requested model, resolved upstream model/provider, TTFB, HTTP status, and secret-redacted stack trace.

---

## Roadmap & Current State

### Currently Working
- **Local Workspace & Native Desktop:** Bun HTTP/SSE daemon, React 19 + Tailwind v4 UI, and Tauri 2 macOS desktop app (native squircle dock icon, streaming UI, voice STT/TTS pipeline).
- **Workspace Modes & Safety:** Chat, strict read-only Plan mode (`.gram/plans/`), and Code mode with `GramFS` overlays, Semantic Graph URIs, and `CodeMode` batched execution.
- **Lyra Cloud Hub (`lyra.2helix.org`):** Better Auth (Google OAuth, email/password, WebAuthn passkeys, Tauri desktop handoff) and Cloudflare Containers deployment.
- **Multi-Provider Gateway & `auto` Router:** Live routing across Amazon Bedrock, Microsoft Azure AI Foundry (`gpt-4o`, `MAI-Thinking-1`, `Phi-4-reasoning`), and OpenRouter with automatic cooldowns, cross-provider failover, BYOK encryption, and ZDR/local-only enforcement.
- **Observability:** Self-healing system checks, structured `RunErrorDiagnostics` drawer, and CloudWatch EMF telemetry on Lyra.

### In Progress
- **Cloud Swarm Offloading at Scale:** Polishing end-to-end AWS ECS Fargate + SQS + Aurora PostgreSQL (`lyra-hub` -> Fargate runner) execution for long-running background tasks.
- **Mission Control Multi-Agent Orchestration:** Tightening DAG swarm visibility, worktree merge previews, and proof-sandbox verification inside the Mission Control drawer.
- **Expanded Model Coverage:** Enabling additional Foundry and Bedrock models as regional quotas and account access expand.

### Future Features
- **Trajectory-Aware Mid-Run Escalation:** Dynamically upgrading or switching models mid-turn based on tool failures, test regressions, or reasoning depth (Switchyard-style trajectory routing).
- **Seamless Cross-Device Workspace Handoff:** Resuming active local/cloud sessions and staged `GramFS` overlays smoothly across desktop and web clients.
- **Richer MCP & Ecosystem Integrations:** Expanding Lyra's MCP server tools (`lyra_dispatch_task`, `lyra_inspect_diff`, `lyra_create_github_pr`) and pluggable workspace skills.

---

## Further Reading

- **[SETUP.md](SETUP.md)** -- Prerequisites, local dev, Tauri builds, tests, and environment variables
- **[ARCHITECTURE.md](ARCHITECTURE.md)** -- Internal data flow and module conventions
- **[docs/desktop-launch-guide.md](docs/desktop-launch-guide.md)** -- Desktop build notes and troubleshooting
- **[Lyra/README.md](../Lyra/README.md)** -- Lyra cloud backend overview

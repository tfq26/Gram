# Gram

Gram is a local-first coding workspace and agent runtime built with Bun, TypeScript, React 19, Vite, and Tauri 2 (with a Rust core crate at `crates/gram-core-rs`).

You run the daemon and workspace UI on your own machine (`http://localhost:7337`), keep your source files on local disk with SQLite-backed overlays and checkpoints, and route model inference through either Lyra Hub (`https://lyra.2helix.org`) or your own BYOK endpoints.

## Prerequisites

- [Bun](https://bun.sh) 1.2+
- [Rust](https://rust-lang.org) (`cargo`) if building the Tauri desktop app or `crates/gram-core-rs`
- Xcode Command Line Tools on macOS (for Tauri native builds)

Install dependencies:

```bash
bun install
```

## Running Gram

### Web + Daemon (Development)

Runs the Bun HTTP/SSE daemon on `http://127.0.0.1:7337` and the Vite dev server on `http://127.0.0.1:5174` (with `/api` proxied to `:7337`):

```bash
bun run dev
```

Or launch only the Bun daemon serving the built frontend (`dist/studio`) on `http://localhost:7337`:

```bash
./launch.sh --web
```

Other launcher modes:

```bash
./launch.sh --server          # Start the HTTP daemon without opening a browser
./launch.sh --cli "prompt"    # Run a single prompt in the terminal
./launch.sh --doctor          # Check local ports, runtime, and environment
./launch.sh --graph           # Index and print sample Semantic AST URIs
```

### Tauri Desktop App

Starts the dev servers (`bun run dev`) and opens the native desktop window pointing at `http://127.0.0.1:5174`:

```bash
bun run tauri:dev
```

Build the production frontend (`dist/studio`) and desktop bundle:

```bash
bun run build
bun run tauri:build
```

For platform prerequisites and troubleshooting, see [docs/desktop-launch-guide.md](docs/desktop-launch-guide.md).

### Checks and Tests

```bash
bun run typecheck   # tsc --noEmit
bun run build:studio # vite build -> dist/studio
bun test            # Bun test suite across tests/ and src/
```

## Workspace Modes

- **Chat**: Read-only workspace answers and web search (`search_web`, `fetch_web_content`). When a project is selected, it can inspect workspace files without modifying them. It can also save explicit user context through the separate personal profile.
- **Plan**: Strictly read-only repository inspection (`read_file`, `list_files`, `search_code`, `find_symbol_definition`, git read tools) to clarify requirements and produce EARS-style specifications (`Experience & User Flow`, `Requirements`, `Design`, `Tasks`) saved under `.gram/plans/` without writing project code files until the plan is approved.
- **Code**: Full read/write file editing, `apply_changeset` multi-file edits, shell execution (`run_command`, background processes), headless browser inspection, and verification runs.

Each project stores a lightweight `.gram/` directory containing `gram.md` (project instructions, detected commands, and gotchas) and `context.json`. Persistent session state, checkpoints, projects, and cached sync deltas live in SQLite (`src/db/schema.ts`).

## Core Primitives

- **GramFS (`src/core/gramfs.ts`)**: SQLite-backed copy-on-write staging overlay and operation log (`opLog`) for isolated edits and rollback checkpoints, paired with Git/Jujutsu detection and worktree isolation (`src/core/worktreeManager.ts`) for async or swarm runs.
- **Semantic Graph (`src/core/semanticGraph.ts`)**: Line-independent symbol URIs (`gram://<filePath>#<kind>:<symbolName>`) and topological impact checks (`blast_radius_simulate`, `semantic_blame_why`).
- **Code Mode (`src/core/codeMode.ts`)**: Executes batched workspace reads and transforms in a single TypeScript evaluation rather than chaining sequential single-tool roundtrips.
- **Context Envelope & Gram Connect (`src/core/contextEnvelope.ts`, `src/sync/`)**: Model-neutral conversation history projection, AHP v2 delta sync (`sinceSeq`), and multi-device presence.
- **Personal Context (`src/db/userContextStore.ts`)**: A local structured profile of user-stated facts, preferences, and goals. Chat, Plan, and Code runs can include a compact profile block; Settings supports review, edits, deletion, JSON export, and a local review queue for facts found in saved chats.

## Authentication, Model Routing, and Privacy

### Personal Context

Personal context is stored in the local runtime SQLite database and is not included in Lyra Hub sync. The model may save a durable fact only when it supplies an exact quote from the current user message. Saved-chat scanning runs locally over user-authored messages and requires review before importing candidates. The profile is included in prompts only when **Settings > Personal Context > Use personal context** is enabled.

### Signing In via Lyra Hub

Gram connects to Lyra Hub (`LYRA_HUB_URL`, defaulting to `https://lyra.2helix.org`) for user authentication and hosted model access:

- **Web browser**: Signing in with Google or email/password sets the `gram_lyra_token` cookie on the local Gram origin.
- **Tauri desktop app**: Clicking **Continue with Google** triggers `POST /api/auth/google` with `{ desktop: true }`, opens your system browser at `${LYRA_HUB_URL}/api/auth/gram-start`, and redirects back through `/api/auth/gram-handoff` to local `/api/auth/handoff?hid=...`. The local handoff page posts the Bearer token to `/api/auth/session-token`, while the desktop app polls `GET /api/auth/handoff-status` and applies the session without requiring a manual restart.

When running an agent turn, Gram extracts `gram_lyra_token` (or the `Authorization: Bearer` header) and forwards it to `${LYRA_HUB_URL}/api/gateway/llm/stream`.

### Model Routing and BYOK

Model selection in `src/providers/router.ts` supports `auto` or explicit pinning across curated **Amazon Bedrock**, **Microsoft Azure AI Foundry** (`gpt-4o`, `MAI-Thinking-1`, `Phi-4-reasoning` via Lyra Gateway), and **OpenRouter** allowlists:

- **Gateway `auto` routing**: By default (when `GRAM_DIRECT_LLM` is unset), `GramAgentRuntime` sends `model: 'auto'` to `${LYRA_HUB_URL}/api/gateway/llm/stream`, where Lyra selects across configured, healthy Bedrock, Foundry, and OpenRouter routes with automatic cross-provider failover.
- **Client-side failover (`src/runtime/agentRuntime.ts`)**: If an upstream model returns a recoverable error (`400`, `403`, `404`, `429`, `502`, `503`, `529`, or invalid/unavailable model errors) before streaming text, Gram marks that model unavailable in-session and retries (up to `modelAttempt < 4`) via `selectOptimalModel()`, skipping `autoEligible: false` speculative models.

Custom OpenAI-compatible or local Ollama endpoints can be added under **Settings > Models** (`src/providers/byokStore.ts`). BYOK API keys are encrypted at rest in SQLite. Privacy modes (`standard`, `zdr`, `local-only`) filter models and enforce zero-data-retention or local-only routing.

## Diagnostics and Troubleshooting

- **Self-healing system checks**: `GET /api/system/checks` checks Lyra Hub connectivity, authentication state, configured providers, and runtime readiness. The UI re-polls checks and automatically clears resolved warnings (for example, once network connectivity returns or sign-in completes).
- **Run error diagnostics**: When a run fails, `GramAgentRuntime` captures a `RunErrorDiagnostics` record containing the requested model (`auto` vs explicit), resolved model ID, provider, loop turn and phase, elapsed time, TTFB, HTTP status, abort signal state on both the HTTP request and runtime controller, upstream Lyra Gateway diagnostics, and a secret-redacted stack trace.
  - Logged to stderr with prefix `[GramRuntime][ErrorDiagnostics]`
  - Appended to `<workspace>/.gram/diagnostics/errors.jsonl`
  - Queryable via `GET /api/diagnostics/errors`
  - Inspectable directly in the UI inside the **Diagnostics** drawer on failed messages (with a **Copy JSON** button)

## Environment Variables

See [.env.example](.env.example) for the full template.

| Variable | Default | Purpose |
| :--- | :--- | :--- |
| `GRAM_PORT` | `7337` | Port for the local Bun HTTP/SSE server |
| `LYRA_HUB_URL` | `https://lyra.2helix.org` | Lyra Hub origin for auth and `/api/gateway/llm/stream` |
| `LYRA_GATEWAY_API_KEY` | unset | Optional headless API key for gateway calls without a user session |
| `GRAM_DIRECT_LLM` | unset (`false`) | Set to `true` to bypass Lyra Gateway and call Bedrock/OpenRouter directly |
| `OLLAMA_BASE_URL` | `http://host.docker.internal:11434` | Local Ollama endpoint for `local-only` or BYOK models |
| `OPENROUTER_API_KEY` | unset | Optional OpenRouter key when calling OpenRouter directly |
| `PARALLEL_API_KEY` / `TAVILY_API_KEY` | unset | Optional web search providers (falls back to DuckDuckGo HTML search) |
| `ASSEMBLYAI_API_KEY` / `ELEVENLABS_API_KEY` | unset | Optional server-side speech-to-text and text-to-speech |

## Repository Structure

```text
Gram/
├── src/
│   ├── cli/                 # CLI commands (`skills`, `doctor`, `graph`, `connect`, `code-mode`)
│   ├── core/                # GramFS, SemanticGraph, CodeMode, ContextEnvelope, KnowledgeGraph, WorktreeManager
│   ├── db/                  # SQLite schema & stores (auth, chat, projects)
│   ├── providers/           # Router, streaming LLM client, secret redaction, BYOK store
│   ├── runtime/             # GramAgentRuntime, tools, process manager, browser tools, unified patch
│   ├── server/              # Bun HTTP server (`index.ts`), routes (`routes/*.ts`), Lyra client bridge
│   ├── sync/                # AHP v2 RPC & Gram Connect presence
│   ├── tools/               # Autonomous skills and specialized workspace tools
│   └── ui/                  # React 19 + Tailwind v4 UI (`App.tsx`, `features/*`, `components/*`)
├── crates/
│   └── gram-core-rs/        # Native Rust crate
├── src-tauri/               # Tauri 2 desktop shell configuration and Rust host
├── docs/                    # Architecture, launch guide, and design specs
└── tests/                   # Bun test suite
```

More detail on internal data flow and conventions is in [ARCHITECTURE.md](ARCHITECTURE.md).

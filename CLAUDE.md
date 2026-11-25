# Gram

Local-first coding workspace and agent runtime (Bun + TypeScript, React 19 / Vite / Tailwind 4 UI, Tauri 2 desktop shell, small Rust crate). Pairs with **Lyra** (separate repo, cloud auth + LLM gateway). See [README.md](README.md), [SETUP.md](SETUP.md), [ARCHITECTURE.md](ARCHITECTURE.md).

## Commands
```bash
bun install
bun run dev            # daemon (:7337) + Vite studio (:5174, /api proxied)
bun run typecheck      # tsc --noEmit
bun test               # all tests in tests/ and src/**/*.test.ts (~6s)
bun test tests/x.test.ts   # single file
bun run build          # typecheck + vite build -> dist/studio
bun run tauri:dev      # desktop app
./launch.sh --doctor   # check ports/runtime/env
```
Run typecheck and `bun test` after changes. Both are currently clean (126 tests).

## Layout
- `src/server/` – Bun HTTP/SSE daemon (`index.ts`, `routes/`, `voice.ts`, Lyra client).
- `src/runtime/` – agent orchestrator (`agentRuntime.ts`), tools (`agentTools.ts`), web/browser tools, process manager.
- `src/core/` – GramFS (SQLite copy-on-write overlay), Code Mode (Worker sandbox), semantic AST graph, context compaction, worktrees.
- `src/providers/` – model registry/router (Bedrock, Foundry, OpenRouter), LLM client, BYOK, secret guard.
- `src/db/` – SQLite stores. `src/sync/` – AHP/Oberon sync. `src/services/externalContracts.ts` – pluggable TTS/STT/web providers.
- `src/ui/` – React app (`App.tsx` (~3.6k lines; voice, layout, project hub and system checks live in `src/ui/hooks/`) renders `features/shell/` panels (Sidebar, ChatHeader, MessageList, Composer, ContextPanel) via props; other features under `features/`). Alias `@` = `src/ui`.
- `src-tauri/` – Tauri shell; `crates/gram-core-rs/` – Rust hashing/patch.
- `tests/` – integration-style tests; unit tests also live beside source.
- `.gram/` – local runtime state (DBs, plans); not source.

## Gotchas
- Config via env (`.env.example`); `.env`/`.env.local` are gitignored. Key vars: `GRAM_PORT`, `LYRA_HUB_URL`, `GRAM_DIRECT_LLM`.
- `gram.*` Code Mode SDK calls are async (RPC to a Worker). Scripts must `await` them; returning a Promise fails with "object can not be cloned".
- The model catalog is owned by Lyra (`/api/gateway/llm/models`). `src/providers/modelCatalog.ts` caches it (built-in 4-model fallback -> `.gram/model-catalog.json` -> live fetch, 6h TTL); `router.ts` does selection on top. `/api/run` rejects ids not in the catalog (or BYOK), but passes unknown ids through while only the built-in fallback is loaded. Legacy ids resolve via aliases (`getModelProfile`). Tests load `tests/fixtures/lyra-model-catalog.json` via `bunfig.toml` preload; regenerate it from Lyra's `listGatewayLlmModels('standard')` when the catalog changes.
- Voice/web providers are global singletons in `externalServices`. Tests that register mocks must reset via `registerDefaultVoiceProviders()` / `registerDefaultWebProviders()` (both clear first).
- `docs/archive/` holds docs for the older .NET codebase (e.g. the 2026-10-05 audit); they do not describe this repo.
- macOS `sed -i` needs `-i ''`.

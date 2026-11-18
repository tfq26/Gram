# Gram Architecture and Developer Guide

This document explains how Gram's backend daemon, agent runtime, desktop shell, auth flow, and diagnostics fit together.

## High-Level Topology

```text
+-----------------------------------+
|  Browser or Tauri Desktop Window  |
|  - Dev: Vite HMR (127.0.0.1:5174) |
|  - Prod: dist/studio (port 7337)  |
+-----------------+-----------------+
                  | HTTP / SSE (/api/*)
+-----------------v-----------------+
|     Bun Daemon (127.0.0.1:7337)   |
|  - src/server/index.ts            |
|  - src/server/routes/*.ts         |
|  - GramAgentRuntime               |
|  - SQLite + GramFS (.gram/)       |
+-----------------+-----------------+
                  | HTTPS + Bearer Session (gram_lyra_token)
+-----------------v-----------------+
|      Lyra Hub (lyra.2helix.org)   |
|  - Better Auth + Google OAuth     |
|  - /api/gateway/llm/stream        |
|  - Cloud Swarm / Escalation       |
+-----------------------------------+
```

In development, Vite (`vite.config.ts`) listens on `127.0.0.1:5174` and proxies `/api`, `/icons`, `/logo`, and static CSS/audio routes to the Bun daemon on `127.0.0.1:7337`. In production (`./launch.sh --web` or `tauri:build`), the Bun server serves the compiled `dist/studio` bundle directly on port `7337`.

## Directory Layout

### Backend and Core Runtime

- `src/db/` -- SQLite database initialization and domain stores:
  - `schema.ts`: `openRuntimeDatabase()` and `initRuntimeSchema()` (`run_checkpoints`, `chat_sessions`, `sync_state`, `workspace_rules`, `projects`, `project_files`, `users`, `user_sessions`, `byok_models`).
  - `authStore.ts`: Local user accounts and session token lookup.
  - `projectStore.ts`: Registered projects, uploaded project files, instructions, and workspace rules.
  - `chatStore.ts`: Persisted chat sessions, run checkpoints, and AHP sequence deltas.
  - `userContextStore.ts`: Local user profile items, settings, exact-quote capture, and locally scanned saved-chat candidates.
- `src/core/` -- Workspace and context primitives:
  - `gramfs.ts`: SQLite-backed copy-on-write overlay and `opLog` for staging and rollback.
  - `semanticGraph.ts`: Symbol indexing with `gram://<path>#<kind>:<symbol>` URIs.
  - `codeMode.ts`: Programmatic TypeScript execution isolate against workspace APIs.
  - `contextEnvelope.ts`: Provider-neutral prompt/history projection.
  - `knowledgeGraph.ts`: Workspace memory and `remember_insight` persistence.
  - `projectInit.ts`: Initializes `.gram/gram.md` and `.gram/context.json` for attached projects.
  - `worktreeManager.ts` and `workflows.ts`: Git worktree isolation for async runs and workflow matching.
- `src/providers/`:
  - `router.ts`: Model catalog (`MODEL_REGISTRY`), allowlist checks (`isAuthorizedModelId`), and `selectOptimalModel()` for `auto` routing.
  - `llmClient.ts`: `streamLlmTurn()` and `streamLyraGatewayTurn()`. Streams SSE from `${LYRA_HUB_URL}/api/gateway/llm/stream`, or calls Bedrock/OpenRouter directly when `GRAM_DIRECT_LLM=true` or when `local-only` Ollama falls back offline.
  - `byokStore.ts`: Encrypted storage for user-configured custom/OpenAI-compatible/Ollama models.
  - `secretGuard.ts`: `redactSecretText()` and path blocking for sensitive files.
- `src/runtime/`:
  - `agentRuntime.ts`: `GramAgentRuntime` orchestration (`executeRun`, `runLlmAgentLoop`, `resumeRun`, error diagnostics).
  - `types.ts`: `AgentRunOptions`, `RunCheckpoint`, `RuntimeStreamEvent`, `RunErrorDiagnostics`, `PendingApproval`.
  - `agentTools.ts`, `browserTools.ts`, `webTools.ts`, `processManager.ts`, `unifiedPatch.ts`, `toolGuides.ts`.
  - `remember_user_context`: Saves an explicitly supported user fact or preference; the compact profile is added to prompts when enabled.
- `src/server/`:
  - `index.ts`: Bun HTTP server, bootstrap endpoint (`/api/bootstrap`), system checks (`/api/system/checks`), error diagnostics (`/api/diagnostics/errors`), SSE run streaming (`/api/run/stream`), and static/studio asset serving.
  - `routes/`: Focused route handlers (`auth.ts`, `files.ts`, `lyra.ts`, `models.ts`, `processes.ts`, `projects.ts`, `userContext.ts`).
  - `lyraClient.ts`: Auth header/cookie extraction (`gram_lyra_token`) and HTTP bridge to `LYRA_HUB_URL`.
  - `http.ts`, `voice.ts`, `classicShell.ts`.
- `src/sync/`: `ahpProtocol.ts` (legacy JSON-RPC + sequence-delta sync), `oberonProtocol.ts` (shared transport contract for future Gram/Lyra sync through Oberon), and `connectPresence.ts` (device mesh tracking).
- `src/tools/`: `agentSkills.ts` and `novelTools.ts`.

### Frontend (`src/ui/`)

- `src/ui/App.tsx`: Main workspace shell, sidebar, project/session state, SSE stream consumer, system check polling/auto-dismissal, and error boundary (`AppErrorBoundary` / `AppErrorScreen`).
- `src/ui/features/auth/`: `AuthPage.tsx` (pixel matrix canvas + sign-in/sign-up + Google desktop/web handoff) and `OnboardingWizard.tsx`.
- `src/ui/features/chat/`: `TraceRows.tsx`, `ChatContextMenu.tsx`, `ShortcutsDialog.tsx`, `chatHelpers.ts`.
- `src/ui/features/settings/`: `SettingsView.tsx`, `settingsConfig.ts`.
- `src/ui/features/workspace/`: `WorkspaceDock.tsx`, `ProjectDialog.tsx`, `FileTree.tsx`, `PlanArtifactCard.tsx`, `ContentStageAndMiniChat.tsx`.
- `src/ui/components/ui/` and `src/ui/components/ai-elements/`: Base interactive controls, message/markdown rendering, and dynamic island status pill.
- `src/ui/lib/`: Shared TypeScript types (`types.ts`), model icons (`modelLogos.ts`), and `cn()` (`utils.ts`).

## Request & Execution Flows

### 1. Chat / Code Run (`POST /api/run/stream`)

1. `App.tsx` submits `{ prompt, sessionId, projectId, modelId, mode, effort, history }` to `POST /api/run/stream`.
2. `src/server/index.ts` resolves `requestedModel`: if `auto`, `selectOptimalModel()` picks the target model (`simple`/`cost` for `chat`, `planning` for `plan`, `balanced` for `code`).
3. `extractRequestAuthToken(req)` reads the `gram_lyra_token` cookie (or `Authorization: Bearer ...` header) and passes it as `authToken` to `runtime.executeRun()`.
4. `GramAgentRuntime.runLlmAgentLoop()` runs up to `MAX_TOOL_TURNS = 20`:
   - Calls `streamLlmTurn()` with `authToken`, `tools`, `effort`, and `controller.signal`.
   - `streamLyraGatewayTurn()` sends `Authorization: Bearer <authToken>` (and `x-lyra-api-key` if `LYRA_GATEWAY_API_KEY` is set) to `POST ${LYRA_HUB_URL}/api/gateway/llm/stream`.
   - Streams `thinking_delta`, `text_delta`, `tool_start`, `tool_result`, `approval_required`, `done`, or `error` back to the client over SSE.
5. On completion, the session and checkpoint are persisted to SQLite, and `syncWithLyraHub(req)` syncs state asynchronously.

### 2. Google OAuth Sign-In (Desktop and Web)

- **Web flow**: `POST /api/auth/google` (with `desktop: false`) returns `${LYRA_HUB_URL}/api/auth/gram-start?redirect=${origin}/`. After Google auth completes on Lyra Hub, `/api/auth/gram-handoff` redirects back with `#auth_token=...`, which the client exchanges via `POST /api/auth/session-token` to set the `gram_lyra_token` HttpOnly cookie.
- **Tauri desktop flow**: WebViews cannot reliably complete third-party OAuth cookies directly inside the embedded window. `POST /api/auth/google` with `{ desktop: true }` generates a `hid` UUID in `pendingDesktopHandoffs`, opens `${LYRA_HUB_URL}/api/auth/gram-start?redirect=${origin}/api/auth/handoff?hid=${hid}` in the user's default system browser, and returns `{ handoffId }`.
  - After Google login in the system browser, the local `/api/auth/handoff` page renders confirmation UI ("Account signed in") and posts `{ token, hid }` to `/api/auth/session-token`.
  - Meanwhile, the desktop app polls `GET /api/auth/handoff-status?hid=...`, receives the token once completed, calls `POST /api/auth/session-token` in the desktop WebView to set its own `gram_lyra_token` cookie, and transitions straight into the workspace without a restart.

## Themes and Contrast Rules

Themes live in `src/server/classic-assets/app.css` and `src/ui/styles.css`:

- `obsidian` (default dark): `#0e0f10` body, `#151719` panels, `#ececec` primary text, `#2b3036` active surface
- `bone` (warm light): `#f7f5f0` body, `#fcfbf8` panels, `#141311` primary text, `#dcd7cb` active surface
- `prism`, `carbon`, and additional presets configured in `src/ui/features/settings/settingsConfig.ts`

**Important UI invariant**: Never hardcode `text-white` or `bg-white/...` on shared shell components (sidebar items, headers, cards). Always use CSS custom properties (`text-[var(--text-primary)]`, `text-[var(--text-muted)]`, `bg-[var(--bg-active)]`, `bg-[var(--bg-hover)]`) so light themes like `bone` keep readable contrast.

## Error Handling and Diagnostics

When `streamLlmTurn` or `GramAgentRuntime` throws:

1. `streamLyraGatewayTurn` attaches `LlmErrorDiagnostics` to the thrown `Error` (`transport`, `lyraUrl`, `modelId`, `provider`, `privacyMode`, `authMethod`, `httpStatus`, `phase`, `elapsedMs`, `ttfbMs`, `eventsReceived`, `signalAborted`, `gatewayDiagnostics`).
2. `GramAgentRuntime.runLlmAgentLoop` catches the error, builds `RunErrorDiagnostics` (combining `requestedModelId`, `resolvedModelId`, `runId`, `turn`, `phase`, `runtimeControllerAborted`, `requestSignalAborted`, `llm`, and redacted `stack`), writes it to `<workspace>/.gram/diagnostics/errors.jsonl`, logs `[GramRuntime][ErrorDiagnostics]`, and emits `{ type: 'error', error, diagnostics }`.
3. `GET /api/diagnostics/errors` returns the 50 most recent persisted error records.
4. `App.tsx` renders failed assistant turns in an error card with a **Retry** button and a collapsible **Diagnostics** panel with **Copy JSON**.

Runtime crashes inside React trees are caught by `AppErrorBoundary`, while connectivity/auth/daemon banners are driven by `GET /api/system/checks` and clear automatically when the underlying check passes again.

## Adding New Code

- **New SQLite table or query**: Update `src/db/schema.ts` and add methods to the matching `src/db/*Store.ts`.
- **New HTTP route**: Add a handler to `src/server/routes/<domain>.ts` and wire it in `src/server/index.ts`.
- **New UI feature**: Place feature components in `src/ui/features/<feature>/`, shared types in `src/ui/lib/`, and theme-token primitives in `src/ui/components/`.

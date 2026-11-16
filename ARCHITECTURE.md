# Gram v2 Architecture & Conventions

## Directory Layout

### Backend & Runtime
- `src/db/` — All SQLite database initialization, migrations, and domain stores:
  - `schema.ts` — `openRuntimeDatabase()` and `initRuntimeSchema()` (`run_checkpoints`, `chat_sessions`, `sync_state`, `workspace_rules`, `projects`, `project_files`, `users`, `user_sessions`).
  - `authStore.ts` — `AuthStore` for local user accounts and session tokens.
  - `projectStore.ts` — `ProjectStore` for projects, uploaded reference files, and workspace rules.
  - `chatStore.ts` — `ChatStore` for chat sessions, run checkpoints, and AHP sync deltas.
- `src/core/` — Core platform primitives: GramFS overlay (`gramfs.ts`), knowledge & semantic graphs (`knowledgeGraph.ts`, `semanticGraph.ts`), context envelope (`contextEnvelope.ts`), code mode (`codeMode.ts`), project initialization (`projectInit.ts`), workflows (`workflows.ts`), and git worktrees (`worktreeManager.ts`).
- `src/providers/` — Model catalog & routing (`router.ts`), Bedrock + OpenAI-compatible streaming (`llmClient.ts`), and BYOK provider store (`byokStore.ts`).
- `src/runtime/` — Agent execution runtime:
  - `agentRuntime.ts` — `GramAgentRuntime` orchestration (delegates persistence to `src/db/`).
  - `types.ts` — `RunCheckpoint`, `RuntimeStreamEvent`, `AgentRunOptions`, `PendingApproval`.
  - `toolGuides.ts` — `loadWorkspaceToolGuides()` workspace context loader.
  - `agentTools.ts`, `browserTools.ts`, `webTools.ts`, `processManager.ts`, `unifiedPatch.ts`.
- `src/server/` — Bun HTTP server (`index.ts`), shared response helpers (`http.ts`), domain route modules (`routes/*.ts`), Lyra Hub proxy (`lyraClient.ts`), voice endpoints (`voice.ts`), and the classic HTML fallback shell (`classicShell.ts`).
- `src/sync/` — Agent Handoff Protocol (`ahpProtocol.ts`) and device presence (`connectPresence.ts`).
- `src/tools/` — Agent skills (`agentSkills.ts`) and novel tooling (`novelTools.ts`).

### Frontend (`src/ui/`)
Organized by **feature** so related components, helpers, and config live together:
- `src/ui/features/auth/` — Sign-in and onboarding (`AuthPage.tsx`, `OnboardingWizard.tsx`).
- `src/ui/features/chat/` — Chat trace, context menu, shortcuts, and helpers (`TraceRows.tsx`, `ChatContextMenu.tsx`, `ShortcutsDialog.tsx`, `chatHelpers.ts`).
- `src/ui/features/settings/` — Settings UI, static config, and localStorage helpers (`SettingsView.tsx`, `settingsConfig.ts`).
- `src/ui/features/workspace/` — Workspace dock, project modal, file tree, and plan cards (`WorkspaceDock.tsx`, `ProjectDialog.tsx`, `FileTree.tsx`, `PlanArtifactCard.tsx`, `ContentStageAndMiniChat.tsx`).
- `src/ui/components/ui/` & `src/ui/components/ai-elements/` — Reusable UI primitives.
- `src/ui/lib/` — Cross-feature types (`types.ts`), model logo mapping (`modelLogos.ts`), and `cn()` (`utils.ts`).

## Conventions

### Where to Put New Files
- **New SQLite table or query**: Add schema/migrations to `src/db/schema.ts` and a domain store in `src/db/<domain>Store.ts`.
- **New HTTP endpoint**: Add to `src/server/routes/<domain>.ts` exporting `handle<Domain>Routes(...): Promise<Response | null>`, returning `jsonResponse` / `errorResponse` from `src/server/http.ts`.
- **New UI feature**: Place feature-specific components, hooks, and helpers under `src/ui/features/<feature>/`; put cross-feature types/helpers in `src/ui/lib/` and generic primitives in `src/ui/components/ui/`.

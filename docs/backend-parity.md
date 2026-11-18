# Gram backend capability parity

This inventory compares the current TypeScript/Rust runtime with the preserved C# backend at Git `HEAD`. It is a backend capability map; the C# UI is out of scope. A C# service or tool is evidence of prior implementation, not by itself evidence that it was enabled or production-ready. Port behavior and security contracts deliberately, not class names or UI coupling.

## Active migration scope

The current implementation brief narrows the work to coding tools, grounded web research, persistent workspace rules, gated local Git, AssemblyAI/ElevenLabs voice, and Gram↔Lyra streaming/swarm/patch integration. Do not port C# presentation or document generation, GramBox container tools, reminders, or C# account/auth/passkey code. Lyra owns identity. The remaining backend categories below are future parity inventory only.

## Current implementation baseline

The TypeScript agent loop currently exposes workspace read/write/list/search, shell execution, and overlay commit tools. It also has GramFS overlays, chat/checkpoint persistence, model routing for Bedrock and OpenRouter, local context/semantic graph modules, and a set of novel tools. These are not all at parity with the old backend: some current modules are not exposed through the model's tool list, while others cover only part of the older service contract.

Web search and public page retrieval are the first backend capability being ported in `src/runtime/webTools.ts` and `src/runtime/agentTools.ts`.

## Capability gaps

| Backend area | C# capability inventory | Current TypeScript/Rust status | Migration direction |
|---|---|---|---|
| Workspace coding | `view_file`, `write_file`, `edit_file`, `apply_changeset`, `create_plan_artifact`, directory/file/grep search, command execution, checkpoint rollback, validation, tests, symbol definition | Basic read/write/list/search/command and GramFS overlay exist. Atomic changesets, plan artifacts, restore points, project validation, test orchestration, and symbol-definition lookup are not equivalent or not agent-exposed. | Port typed file edits, reviewable change sets, rollback, diagnostics/test execution, and symbol navigation as independent backend services/tools. |
| Git and code hosting | Repository discovery/read, file commit, PR creation, provider abstraction | Basic local workspace access only; no equivalent Git provider/action tools in the current agent loop. | Add a Git service interface and explicit, permission-gated write actions. |
| Web research | `search_web`, `fetch_web_content`; planning, source ranking, primary-source policy, grounding/evidence validation, diagnostics, and search tests | New search/fetch implementation underway; no complete grounding/evidence pipeline yet. | Finish safe search/fetch, then port planning, result quality, source tracking/citations, budgets, and reliability behavior. |
| Agent workflows | Intent routing, workflow recipes, tool selection/stripping policy, bounded tool loops, self-healing validation | A bounded tool loop and basic workspace prompt exist; no comparable workflow router or self-healing gate. | Port policy as composable runtime middleware; keep tools scoped to task and model capabilities. |
| Subagents and task management | Spawn/status/list subagents, task management, checkpoints and resumable workflows | Runtime supports persisted runs/checkpoints and cancellation/resume; no equivalent subagent orchestration or task/memory tool set. | Port resumable worker orchestration after permission, cancellation, and isolation semantics are defined. |
| Memory and rules | Remember rules, structured memory, extraction, context building, task/reminder services | Workspace rules and KnowledgeGraphStore exist. User Context now stores a local structured profile with exact-quote capture and reviewed extraction candidates from saved chats; cross-device profile sync and broader semantic extraction remain future work. | Keep user profile, project knowledge, and workspace rules in separate scopes; add sync only with an explicit privacy and encryption design. |
| Sandboxes | GramBox lifecycle, exec, files, diff, checkpoints, rollback, merge preview; scheduling, network/path policy and audit | GramFS copy-on-write layer exists, but is not a containerized, network-governed sandbox or multi-job scheduler. | Treat as a distinct sandbox backend; preserve egress controls, path guards, audit, and merge preview. |
| Tool ecosystem and connectors | Toolbox/MCP tools, connector discovery, credential vault, action execution, permission and egress policies | Static tool list only; no equivalent dynamic connector runtime or permissioned connector host. | Port connector contracts and security boundary before adding third-party actions. |
| Databases and storage | Read-only SQL and gated DML tools; SQLite/Postgres connectors; S3/object storage providers | Local SQLite is used for Gram runtime persistence; no user-database tool or object storage integration. | Keep application persistence separate from user-connected data; port read-only query first, then explicitly authorized writes. |
| Documents and presentations | Document ingestion/parsing; presentation generation, revisions, status/logs, HTML assembly | No equivalent document or presentation backend in current TypeScript runtime. | Port parsers and generation services as standalone capabilities only if still in product scope. |
| Voice and media | Streaming transcription, AssemblyAI/ElevenLabs providers, speech services, image search | Browser speech-recognition asset exists; no equivalent server-side speech provider pipeline. Image search code existed but was not one of the active agent tools. | Port provider interfaces, cancellation, and privacy/configuration behavior independently. |
| Models and usage | Multiple provider gateways, model capability probing, key management, quota/rate limits, prompt caching | Bedrock/OpenRouter clients and a model registry exist; no full provider gateway/capability probe/quota system parity. | Expand providers behind a common contract and add provider-independent capability/usage records. |
| Auth and account services | Auth providers, passkeys/password recovery, backend authorization, session storage | Current TypeScript shell has app auth/sync integration, but not the old backend's full auth and authorization surface. | Compare deployment modes and security boundaries before porting identity features; avoid UI-specific coupling. |
| Diagnostics and reliability | Workspace diagnostics, compiler/test result parsing, search telemetry, retries/rate limits, audit services | Some command output and runtime health tools exist; no full diagnostic or reliability service parity. | Port structured results, cancellation/timeouts, rate limits, and audit records as shared primitives. |

## Prior C# agent tool surface

These were active tool definitions in the old `AgentTools.cs`, grouped for migration tracking:

- Workspace: `view_file`, `write_file`, `edit_file`, `apply_changeset`, `create_plan_artifact`, `list_directory`, `find_files`, `grep_search`, `run_command`, `rollback_checkpoint`, `validate_project`, `run_tests`, `find_symbol_definition`.
- Git: `git_list_repos`, `git_read_file`, `git_commit_file`, `git_create_pr`.
- Research: `search_web`, `fetch_web_content`, `resolve_capability`.
- Toolbox and sandboxes: `toolbox_list_tools`, `toolbox_execute_tool`, `grambox_create_sandbox`, `grambox_exec`, `grambox_list_sandboxes`, `grambox_write_file`, `grambox_read_file`, `grambox_get_diff`, `grambox_commit_sandbox`, `grambox_preview_merge`, `grambox_close_sandbox`, `grambox_checkpoint`, `grambox_rollback`, `grambox_status`.
- Artifacts: `create_presentation_project`, `revise_presentation_section`, `get_presentation_status`, `get_presentation_logs`, `assemble_presentation_html`.
- Agent support: `spawn_subagent`, `get_subagent_status`, `list_subagents`, `manage_tasks`, `remember_rule`, `set_reminder`.
- Connected data: `db_list_tables`, `db_inspect_schema`, `db_query_readonly`, `db_execute_dml`.

The old backend also contains services with no corresponding model-callable tool, so the list above is not the entire parity target. The service inventory includes auth/authorization, connectors, storage, document parsing, speech, project and conversation persistence, research grounding, sandbox controls, quota/rate limiting, and diagnostics.

## Port order

1. Complete grounded search and the local coding tools with transactional overlay edits, validation, symbol lookup, persistent rules, and gated Git actions.
2. Complete server-side voice transcription/TTS, with cancellation and concise spoken summaries.
3. Complete Lyra streaming, swarm escalation/status, patch import into a review layer, and task diff/tool inspection.

Each port should retain a focused backend contract and security policy, then be surfaced through the TypeScript runtime only when that contract is implemented. UI-only components are intentionally excluded.

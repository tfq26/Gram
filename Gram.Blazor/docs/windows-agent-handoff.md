# Gram development handoff — 2026-09-04

## Start here

This document transfers the development context from Codex thread `01a049c7-aa6b-7dd3-a4ff-de4560288f5e` to an agent on Windows. It does not require access to the Mac rollout file. Repository: `https://github.com/tfq26/Gram.git`, branch `main`. Audited base commit: `0848f6b` (before this documentation commit). The working tree was clean at audit time. All paths below are relative to the repository root unless stated otherwise.

Read the code before implementing. Earlier conversational reports sometimes described scaffolding as complete features and builds as verification of runtime security. The limitations below supersede those claims. This handoff itself is a documentation change, not a remediation of those limitations.

On Windows, clone/pull the repository, enter `Gram.Blazor`, inspect current changes, and build the projects listed below. Never overwrite another agent's edits. macOS app bundles and machine-local data are not a Windows deployment mechanism. Configure credentials locally; do not copy secrets from conversation history into source control.

## User intent and architecture decisions

Gram is an assistant with text and voice, grounded research, structured personal/project memory, extensible connected actions, and a desktop coding experience. Maintain the existing web application while developing desktop and future mobile clients. Backend workflows and contracts are shared; frontends may differ visually.

- Keep the backend in the existing ASP.NET Core process initially. Versioned REST resources and commands live under `/api/v1`; SSE streams run events with sequence numbers and replay.
- Hosted PostgreSQL is accessed through the backend API, never directly by desktop/mobile. SQLite is the local backend option. Do not make desktop require its own PostgreSQL installation.
- Privileged files, terminal, browser profile and future computer control belong to a paired local agent. Merely checking that a backend URL says localhost is not authentication or proof of permission.
- Use native service adapters and MCP behind one registry, policy, validation and audit boundary. Prefer an existing capability over generating another connector. A generic HTTP POST and managing a Postman collection are different tasks.
- Prefer existing verified OAuth connections. Browser fallback requires an explicitly paired profile and confirmed account; do not extract cookies/passwords or automate MFA. Do not silently substitute another account.
- Home Assistant is the first smart-home target. Signed webhook ingestion comes before arbitrary event-triggered automation. Purchases, messages, account changes and high-impact devices require concrete approval.
- Internal UI region names are Canvas, Infosheet, Sidebar, Menubar. Do not expose these internal names as user-facing labels.
- Presentation work was deferred until after 1.0; its source remains and its feature flag is off by default. Mobile is future work. `desktop-use` is a disabled future-release scaffold.
- MAI-Transcribe-2 was research only. Do not replace AssemblyAI/ElevenLabs without a new request and realtime evaluation.

## What exists and what is incomplete

### Shared backend and desktop

`Shared/BackendContracts.cs`, `BackendClient.cs`, `BackendFacade.cs`, `RunManager.cs`, `AgentWorkflows.cs`, and `AgentRunTracking.cs` implement the REST/SSE and workflow foundation. `clients/Gram.Desktop` contains a Photino.Blazor shell, `DesktopSession`, settings, chat, tabs, and platform styles. It still references the full web project rather than an independent contracts/client assembly.

The live event shape is `run.step`, with a JSON string payload containing `contentDelta`, `thinkingDelta`, `toolCall`, `errorMessage`, and `isComplete`. Desktop handles `contentDelta`; historical handoff examples using only `text.delta` were wrong. Keep provenance and sequence when replaying; verify reconnect/dedup behavior rather than assuming it exists. Check cancellation response bodies: some backend commands return empty successful responses while the generic client expects JSON.

A real CLI smoke test previously produced `DESKTOP_PONG` using Gram Q+. The native GUI was reported running on the host Mac by another agent; this session did not independently inspect it. At one point the selector was changed from `#app` to `app` based on a sample: verify the selector against the actual root element in `wwwroot/index.html` before claiming the GUI works. A sample using an `<app>` element does not prove `#app` is invalid for `<div id="app">`.

Root `scripts/launch-desktop.sh` opens the existing `Gram.app` when present and does not rebuild it. `scripts/bundle-macos-app.sh` publishes macOS ARM64 and recreates the bundle. Review that script before running because it removes the old bundle. It is not a Windows packager. The launcher has used `exec` in CLI mode (bypassing its cleanup trap), and opening a detached app can also immediately trigger cleanup of a backend it just started. Reinspect before fixing.

`0848f6b` includes work by another agent on permissions/security and desktop sync. Inspect `AgentSecurityService`, tool execution and run permission events before adding a duplicate permission engine. Existing local capability enablement passes a workspace and boolean from the desktop; enforcement must ultimately be server/local-agent owned. RunManager subscribes to shared permission events: verify concurrent runs cannot receive another run's prompts.

### Models and voice

Latest intended mapping in `Shared/ModelIds.cs` and the model services:

- Gram Q: `gemini-3.1-flash-lite` via Google.
- Gram Q+: `meta/muse-spark-1.3-contributor` via Vercel.
- Gram Q++: Gemini compatibility alias; not a distinct premium engine.
- Groq `openai/gpt-oss-120b` key retained for future use.
- Ling Free alias currently points at `inclusionai/ling-3.0-flash-fin-free`; the user was not enthusiastic about this model. Do not prioritize it for coding. The earlier Tiny Free ID returned 404.

Muse's first review exhausted a 5,000-token hidden-reasoning budget without a visible answer; `reasoning_effort: none` produced a useful four-finding review. This was not a rigorously equal-settings benchmark. Check that BOTH simple streaming and orchestrator payloads apply the intended Muse setting; the audit found the explicit Muse condition in ModelService but not the orchestrator's reasoning branch. Keep cloud provider secrets on the backend.

### Research

`WebSearchProviders.cs` implements Parallel (`PARALLEL_API_KEY`) and Tavily (`TAVILY_API_KEY`); Parallel is registered first. `WebContentService` supplies search, fallbacks/cache and HTML extraction. `ResearchEvidenceService` adds ranked sources, IDs, bounded excerpts, page fetches (2 normal/4 complex), literal coverage checks and `IsReady`. `/api/v1/research` exposes it. AgentOrchestrator prefers it for prefetched research; ConnectorSdk can attach a packet when explicitly given a research question.

Actual live Parallel probes returned Japan-capital sources and Postman API/authentication/webhook pages. However, a Postman-hosted Zendesk collection was also returned: an official hosting domain does not establish first-party authorship. OAuth-in-Postman documentation is not evidence of OAuth support for the Postman management API. Results establish discovery, not correct implementation requirements.

Remaining research work:

- Fix topic/query planning that appends hardware rumors/keynote queries to unrelated current/API questions; remove broad topic allowlists that exclude the requested vendor's docs.
- Enforce requested source restrictions across ALL providers, fallback results and page redirects; include policy fields in search cache keys.
- Fetch relevant sections beyond navigation boilerplate; deduplicate mirrors and canonical URLs; parallelize within a bounded fetch budget.
- Treat quality scores and keyword coverage as heuristics, not factual verification. Current readiness does not establish endpoint schemas, signatures, rate limits, scopes or contradiction resolution.
- Citation validation currently accepts other URLs on a retrieved host. Require actual retrieved evidence and distinguish citation provenance from claim entailment.
- Ensure normal tool-driven search, research workflow and connector research use one path. Failed/incomplete packets should not make the model invent missing details or lose all ability to follow up.
- Add deterministic HTTP fixtures and opt-in live probes that assert expected documentation sections and record provider, source URLs, coverage gaps and latency. Never treat a successful build as a research evaluation.

### Structured memory

`StructuredMemoryModels/Service`, `MemoryExtractionService`, `ContextBuilder`, `/api/v1/memory*`, `IMemoryApi`, the web `/memory` page and desktop Infosheet contain the initial system. It stores subject/predicate/value, kind, confidence, importance, source/evidence, candidate/active/superseded/forgotten status. Regex extraction handles a few explicit patterns after successful runs. Service tests demonstrate retrieval without carrying the previous transcript.

This is still a JSON vault, NOT the proposed PostgreSQL structured-memory system. Critical unfinished behavior:

- Missing user IDs in service calls can search/list/mutate across users. ContextBuilder does not supply an authenticated user; RunManager extraction defaults to `default`. Pass a trusted identity snapshot through background execution and scope every operation; avoid relying on HttpContext after request completion.
- Null project filters currently include all projects. Define personal versus project retrieval explicitly, with no implicit personal relationship disclosure into shared/project contexts.
- New candidates can supersede an active fact before approval. Confirm should transactionally resolve supersession; rejection must preserve prior active truth.
- Retrieval can score unrelated memories positively; enforce relevance and time validity. Do not assume pronoun/entity resolution exists.
- Forget currently tombstones data without removing evidence/content. Define deletion across active records, history, caches and backups, and accurately disclose retention.
- Persist via PostgreSQL/SQLite with optimistic concurrency, atomic transitions and safe migration. Do not assign legacy `default` records to an arbitrary authenticated account.
- Client confirm/reject helpers expect JSON despite empty successful endpoint bodies. Add API tests for this and actual cross-user, cross-project and cross-process behavior.
- Broader extraction, confirmed sensitivity policy, memory disable/edit/export, entity relationships, consolidation and meaningful multi-conversation tests remain.

### Connector catalog, routing and settings

`ConnectedActionModels.cs` has `IConnectedConnector`, registry/action/connection contracts; `ConnectorRegistry.cs` stores runtime registrations. `CapabilityIntentRouter` is a keyword heuristic exposed at `/capabilities/resolve` and as the `resolve_capability` tool. It is advisory; it is not yet the complete workflow/execution planner.

`BuiltInConnectors.cs` includes reminders, controlled HTTP, and a functioning Spotify Web API adapter. Home Assistant, Postman and MCP currently expose action catalogs and return `connection_required`; they are not implemented provider integrations. `desktop-use` exposes future observation/control descriptors and refuses execution unless the feature flag is enabled, but no local computer-control agent exists. MCP registration UI does not prove MCP sessions/tool discovery work.

The web and desktop SettingsView both have a Connectors tab and load shared connector/connection metadata. Spotify has Connect/Reconnect UI; the link must use the secure explicit loopback callback below. Finish truthful action-level status and setup UX for the stub connectors, and make desktop connection refresh/disconnect use the shared backend APIs rather than local-only metadata. Do not call stub integrations “Available” in a way that implies execution works.

Router gaps: case-sensitive helper matching, substring false positives, no schema/capability compatibility search, misleading generation recommendation when an existing connector lacks a matched phrase, and no normal model tool for the complete approval/execution lifecycle. Missing credentials should request connection/reauthorization, not generate another connector. Unknown service auth requirements must be researched from primary docs.

### Connections, approvals and credentials — production blockers

`ConnectorConnectionResolver.cs` persists JSON metadata and checks expiry/scopes. Spotify OAuth is implemented in `SpotifyOAuthService.cs`: state/PKCE, token exchange, `/v1/me` verification, refresh, disconnect, and encrypted access/refresh-token storage. Spotify actions bind the selected verified connection and the action endpoint attempts refresh on expiry. The implementation still trusts manually registered scopes, needs refresh-token concurrency control, and must reject an explicitly requested unknown connection instead of silently choosing another. Generic OAuth authorization/callback, account verification, revocation, and scope handling for Google/Postman/Home Assistant remain unfinished.

`BackendAuthorizationFilter` permits a local mode based on the HTTP Host header, which is not proof of local origin. Token validation does not visibly establish claims for `GetMemoryUserId`; verify identity propagation and remove insecure `default` fallback for hosted actions. Package APIs also lack per-user package ownership.

`ConnectorPermissionService` caches only Session approvals; Once/Run are not consumed or enforced. Keys omit connection, parameters, actual session and target. Deny does not remove a previous grant. Execution uses risk levels but can overlook `RequiresApproval` on safe actions and disabled descriptors. Bind approvals to immutable action previews and concrete account/target/payload; consume Once atomically; scope Run/Session and deny/expiry correctly.

`ConnectorCredentialVault` encrypts with a key derived from username/machine name plus a fixed salt. This is predictable and not suitable for production secrets. It also returns empty state on decryption errors, risking overwrites, and colon-composed identifiers can collide. Replace with authenticated encryption backed by a securely provisioned key/OS key store or managed vault; atomic persistence, exact ownership and key rotation are required. No secrets should be committed or printed.

### Generated SDK, package lifecycle, host and egress

`ConnectorSdk` generates a C# template whose ExecuteAsync returns `not_implemented`; it does not use an LLM to research and implement a functioning adapter. String blocklists are not security validation. `CanInstall` can be true for a stub. Research metadata is optional and submitted previews are not trusted evidence of validation. The SDK now accepts a research question and attaches a `ResearchEvidencePacket`, but it does not yet synthesize provider-specific code from that packet.

`ConnectorPackageService` stages JSON/source and runs `dotnet build`, records state and copies artifacts on approval. Build is a host process, NOT sandbox compilation; inherited credentials/build configuration, mutable package contents, concurrent replacement and artifact provenance need protection. Drain output concurrently and bound it while compiling. Verify source/artifact hashes, immutable versions, all dependencies, cancellation, approval binding and per-user ownership before installation. No package compile/run tests have established a safe arbitrary-code boundary.

`ConnectorRuntimeService` registers a manifest proxy to `IConnectorHostClient`; health currently describes registration, not worker/provider readiness. Registry mutation can overwrite built-ins. Activation state is in memory and rollback selects by timestamps rather than verified last-known-good activation history. Add reserved IDs, ownership, durable lifecycle state, health checks, transactional activation and rollback tests.

Projects `clients/Gram.ConnectorHost` and `clients/Gram.ConnectorWorker` exist. The host accepts a secret-authenticated request, validates the installed manifest, and launches a worker per action over stdin/stdout. Backend-to-host supports Unix sockets or loopback TCP through `IConnectorHostClient`. Windows named pipes and hosted mTLS/workload identity remain unimplemented. Socket startup deletes an existing path; verify ownership/type and use a private directory rather than blindly deleting it.

Important: workers are NOT OS sandboxed. `GRAM_CONNECTOR_NETWORK_POLICY=deny/proxy-required` is only an environment string; generated code can ignore it, open sockets, read local files or spawn processes. Memory/CPU polling monitors the immediate process, not a hard aggregate process-tree quota. Output uses ReadToEndAsync before checking size, leaving unbounded buffering. Cancellation/process teardown and CPU allocation require real adversarial tests. Credentials on the same filesystem may still be readable by the worker's OS account.

The egress endpoint checks a manifest allowlist and resolves DNS, then a fresh HttpClient resolves again and follows redirects. This does NOT prevent DNS rebinding, redirect SSRF or secret forwarding. Pin approved addresses at connection time, validate every redirect (or disable redirects), reject reserved/mapped/private ranges and forbidden ports, use bounded streamed bodies and exact credential destination bindings. Avoid blanket subdomain authorization. The separate ControlledHttpConnector currently bypasses these egress checks and only rejects a few literal private addresses.

Egress HMAC tokens expire but are reusable, not one-time. They are not bound to a concrete action/method/path/body or revocable execution state. Header credential bindings need validation and origin/scope constraints. Provider responses can echo secrets, so “workers never receive secrets” is not established just because injection happens in a broker.

## Recommended continuation order and acceptance gates

1. Live-test Spotify OAuth and actions with the user’s account. Required callback: `http://127.0.0.1:7337/api/v1/connectors/spotify/callback`; `localhost` is invalid. Test status, search, play, pause, next, refresh, disconnect, no-device, revoked-token, missing-scope and wrong-account cases.
2. Fix trusted identity propagation, package/connection ownership, concrete approvals, verification-only connection readiness and production credential storage BEFORE broader real-account execution.
3. Route Spotify and Controlled HTTP through one hardened egress implementation; disable direct egress for generated workers. Add kernel/container network restrictions, redirect validation, output streaming limits, and aggregate process-tree resource limits.
4. Implement a real Postman adapter from a validated research packet, then Home Assistant, MCP transport/discovery, and verified webhook ingestion/replay protection. Keep stubs disabled until each has its own contract and live-safe fixture tests.
5. Complete package provenance, isolated compilation, durable lifecycle/audit state, restart/rollback behavior, Windows named pipes, and hosted mTLS/workload identity.
6. Repair research planning/extraction/evidence evaluation and make connector generation use it as a required input. Continue structured-memory database migration, user/project isolation, and cross-client synchronization.

Definitive connector scenario: an ordinary text/voice request resolves an existing approved generic HTTP capability for a plain POST, but selects or proposes a Postman adapter for Postman-specific management. If missing, research exact first-party APIs, generate real implementation, compile/test in isolation, review permissions/install, connect/verify the account, preview the concrete action, approve/execute once, verify result and persist audit. Repeating it reuses the installed connector and intended account. Test missing scopes, revoked credentials, wrong user/account, duplicates, timeout, restart and rollback. Never use a paid/destructive real endpoint merely to prove connectivity.

## Verification and Windows commands

From `Gram.Blazor`:

```text
dotnet build Gram.Blazor.csproj
dotnet build clients/Gram.Desktop/Gram.Desktop.csproj
dotnet build clients/Gram.ConnectorHost/Gram.ConnectorHost.csproj
dotnet build clients/Gram.ConnectorWorker/Gram.ConnectorWorker.csproj
dotnet test tests/Gram.Blazor.UnitTests/Gram.Blazor.UnitTests.csproj -m:1 --filter "Category!=LiveRun"
dotnet run --project Gram.Blazor.csproj
dotnet run --project clients/Gram.Desktop/Gram.Desktop.csproj -- --backend http://localhost:7337
```

Historical verified baseline was 145 discovered tests, 144 passing with one LiveRun excluded. Additional connector/memory tests were added afterward; recount rather than quoting that old number. Many recent steps only compiled. This handoff audit did not rerun builds/tests. Test runner socket errors inside Codex are environment failures: use a narrowly scoped approved escalation when needed rather than disabling all sandbox protections.

Live Parallel searches previously succeeded after network escalation. Use the existing configured key without printing it. Do not source/export every secret unnecessarily. Local `.env`, launch profiles, credential vault data and auth tokens are not handoff artifacts. Model-provider/account availability should be retested only when needed and authorized.

Useful entry documents: `Gram.Blazor/docs/backend-architecture.md`, `desktop-frontend-handoff.md`, `search-architecture.md`, `client-platform-plan.md`, and this handoff. Treat their statements as intended direction where code does not yet support them. The current priority is completing the connector settings request and closing the documented execution/auth gaps before calling the connector platform production-ready.

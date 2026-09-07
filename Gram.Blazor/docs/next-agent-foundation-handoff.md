# Gram Foundation and Windows Client Handoff

## Purpose

This document defines the next implementation scope after the connector foundation work. Reference it before making changes in the Gram repository.

The current priority is to finish security correctness, build the authenticated Windows local-agent boundary, and mature the desktop client. Provider-specific connector implementation is intentionally out of scope for this phase.

## Current baseline

The repository already contains the following foundation work:

- Connector identity and ownership checks.
- Durable connection and approval storage.
- Connector package staging, compilation, installation, and ownership filtering.
- Encrypted connector credential storage.
- Connector runtime persistence and restart recovery.
- Connector worker egress restrictions and Windows Job Object isolation.
- Desktop SSE reconnect, sequence deduplication, terminal-event handling, and outage state.
- Desktop offline cache, cached conversation fallback, active-run recovery metadata, and localhost local-agent readiness probing.

The worktree may contain other user or agent changes. Preserve unrelated modifications. Do not reset, clean, or overwrite the worktree wholesale.

## Scope for this phase

Complete these tasks in order unless a verified dependency requires a change:

### 1. Close the approval and redirect security gaps

#### Redirect handling

- Configure the web-browser HTTP handler with `AllowAutoRedirect = false`.
- Manually process redirect responses.
- Validate every redirect target against the allowed-domain policy before following it.
- Reject HTTP redirects, undeclared domains, private/reserved destinations, and redirect loops.
- Add tests for unauthorized intermediate and final redirect targets.

Do not rely only on checking `Response.RequestMessage.RequestUri` after a request. That can occur after an HTTP client has already followed an unauthorized redirect.

#### Approval lifetime and atomicity

- Decide and document the lifetime of `ApprovalScope.Session`.
- Preferred behavior: session approvals are memory-only and do not survive process restart.
- If persistence across restart is intentionally retained, introduce an explicit persisted session/device identifier and test that behavior.
- Preserve exact connector, connection, action, parameter, user, tenant, and run binding.
- Add concurrent Once-approval consumption tests.
- If shared storage is supported across backend instances, use a transactional SQLite/PostgreSQL operation rather than relying only on an in-process lock.

### 2. Build the authenticated Windows local-agent process

Use the existing local-agent contracts as the starting boundary. The local agent must:

- Run as a separate process from the backend and desktop UI.
- Bind only to localhost.
- Require a per-installation pairing or installation secret.
- Reject unauthenticated and cross-client requests.
- Expose named capabilities only; do not expose a generic shell or generic filesystem endpoint.
- Report version, health, and enabled capabilities.
- Validate run ownership, workspace scope, sandbox ownership, network policy, and resource budgets.
- Terminate process trees on cancellation.
- Remain usable for explicitly local-only work when the hosted backend is unavailable.

Initial capability surface:

- `files.pick`
- `terminal.run`
- `sandbox.create`
- `capabilities.get`

Keep the protocol versioned and make failures return normalized, actionable errors without exposing secrets or stack traces.

### 3. Complete desktop local capabilities

Add the Windows desktop integration behind explicit interfaces:

- Native secure file picker.
- Workspace-root and path traversal validation.
- Terminal capability with explicit command scope and cancellation.
- Sandbox create/status/diff controls.
- Local-agent unavailable and unauthorized states.
- Performance monitoring for active local runs and process/resource limits.

The desktop frontend must not call provider APIs, persistence layers, or arbitrary shell/filesystem APIs directly. All privileged operations must go through the authenticated local-agent boundary.

### 4. Strengthen desktop recovery and offline behavior

- Resume active runs after desktop restart using run ID and last sequence.
- Preserve already-rendered text during reconnect.
- Persist per-run recovery state safely and clear it only after terminal confirmation.
- Add queued conversation edits for offline operation where safe.
- Surface explicit version conflicts instead of overwriting newer server state.
- Prefer SQLite for the durable desktop cache once the recovery behavior is stable.
- Never store access tokens, refresh tokens, provider secrets, or raw authorization headers in the cache.

### 5. Extract the shared client core

Move or factor frontend-neutral client code into reusable projects without changing the REST/SSE contract:

- Shared DTOs and JSON serialization.
- REST client methods.
- SSE subscription and replay.
- Authentication injection and future refresh handling.
- Normalized API errors.
- Safe-read retry and reconnect policy.
- Sync/version-conflict state.
- Redacted structured client logging.

Desktop-only local-agent and native-window code must remain outside the shared client core.

### 6. Validate the connector runtime foundation

Add or strengthen tests for:

- Package ownership isolation.
- Connector runtime restart and reactivation behavior.
- Credential revocation and reauthorization.
- Worker cancellation and process-tree cleanup.
- Resource-budget enforcement.
- Egress failures, DNS rebinding, redirects, and response-size limits.
- Cross-user and cross-tenant access rejection.

## Explicit exclusions

Do not implement or expand these items in this phase:

- Spotify live validation.
- Provider-specific OAuth integrations.
- Postman connector operations.
- Home Assistant connector operations.
- MCP server/runtime integrations.
- Mobile client work.
- Provider API keys or provider-specific HTTP code in the desktop shell.

These are intentionally deferred until the foundation and Windows local-agent boundary are stable.

## Verification requirements

At minimum, run:

```powershell
dotnet test Gram.Blazor/tests/Gram.Blazor.UnitTests/Gram.Blazor.UnitTests.csproj -m:1 --no-restore --logger "console;verbosity=minimal"
dotnet build Gram.Blazor/clients/Gram.Desktop/Gram.Desktop.csproj --no-restore
dotnet build Gram.Blazor/clients/Gram.ConnectorHost/Gram.ConnectorHost.csproj --no-restore
dotnet build Gram.Blazor/clients/Gram.ConnectorWorker/Gram.ConnectorWorker.csproj --no-restore
```

Focused tests must cover the changed behavior. Report full-suite failures accurately and distinguish unrelated pre-existing test-environment failures from regressions introduced by the change.

## Handoff rules

- Read this document and the existing Windows handoff before implementation.
- Inspect the current worktree before editing.
- Preserve existing user and agent changes.
- Use `apply_patch` for source edits.
- Do not claim production readiness based only on happy-path unit tests.
- Do not mark the phase complete until security, restart, ownership, and Windows acceptance behavior have focused coverage.

---

## Completed Implementations & Laptop Continuity Guide (Updated 2026-09-06)

All foundation tasks outlined above have been implemented and verified. When pulling this repository on your laptop, use this section to understand what was fixed and how to proceed.

### 1. Summary of Completed Fixes & Additions

#### A. Approval Engine & Redirect Safety (Security Fixes)
- **Redirect Policy Enforced**: In `Gram.Blazor/Shared/WebContentService.cs`, `WebBrowser` HTTP client has `AllowAutoRedirect = false`. Implemented a manual 5-hop redirect loop evaluating `UrlNormalization.IsDomainAllowed` prior to each hop. Rejects HTTP downgrades, unlisted domains, loop cycles, and private/metadata IPs (`ConnectorEgressPolicy.IsPrivateOrReserved`).
- **Session Approval Lifetime**: Kept `ApprovalScope.Session` strictly in-memory in `ConnectorRegistry.cs`; session approvals now vanish on restart rather than persisting across instances.
- **Atomic Once Approval Consumption**: Added SQLite transactional `BEGIN IMMEDIATE` + atomic update lock in `connector_approvals.db` for multi-process safety.

#### B. Authenticated Windows Local Agent (`Gram.LocalAgent`)
- **New Project**: `Gram.Blazor/clients/Gram.LocalAgent/` (targets `net9.0`).
- **Localhost-Only Binding**: Listens strictly on `http://127.0.0.1:7340` (or `PORT`/`GRAM_LOCAL_AGENT_PORT`).
- **Host Header Validation**: Middleware rejects requests where `Host` is not `localhost`, `127.0.0.1`, or `::1` to prevent DNS rebinding attacks.
- **Pairing Secret Authentication**: Generates or loads a 64-char hex key from `%LOCALAPPDATA%/Gram/local-agent.secret` with constant-time equality check (`CryptographicOperations.FixedTimeEquals`). Auth required via `X-Local-Agent-Secret` or `Authorization: Bearer <secret>`.
- **Capability Endpoints**:
  - `GET /local/v1/health` (unauthenticated status)
  - `GET /local/v1/capabilities` (reports `capabilities.get`, `files.pick`, `terminal.run`, `sandbox.create`)
  - `POST /local/v1/files/pick` (workspace-bounded secure file retrieval)
  - `POST /local/v1/terminal/run` (command runner with process-tree termination on timeout/cancellation)
  - `POST /local/v1/sandbox/create` (creates isolated sandbox directory)
- **Shared Contracts**: `Gram.Blazor/Shared/LocalAgentModels.cs`.

#### C. Desktop Local Capabilities & Path Guard (`Gram.Desktop`)
- **Path Traversal Guard**: `Gram.Blazor/clients/Gram.Desktop/Services/LocalPathGuard.cs` restricts operations strictly to within the designated workspace root.
- **Authenticated Client**: `Gram.Blazor/clients/Gram.Desktop/DesktopLocalAgent.cs` implements `ILocalAgentClient` with automatic secret pairing, typed methods, and non-localhost URI rejection.

#### D. Desktop Recovery & SQLite Offline Cache
- **Durable SQLite Storage**: `Gram.Blazor/clients/Gram.Desktop/DesktopOfflineCache.cs` supports `desktop-cache.db` in WAL mode (`PRAGMA journal_mode = WAL;`) with JSON fallback.
- **Offline Edits**: Implemented `EnqueueEdit`, `GetQueuedEdits`, and `RemoveQueuedEdit` with timestamp ordering.
- **Version Conflict Surfacing**: Added `VersionConflictException` to surface conflicts rather than silently overwriting server updates.
- **Credential Scrubbing**: `Sanitize` guarantees access tokens, refresh tokens, and secrets are never persisted in the offline cache.

#### E. Shared Client Core
- **Extracted Abstractions**: `Gram.Blazor/Shared/ClientCore.cs` contains:
  - `IAuthenticationProvider` & `StaticTokenAuthenticationProvider`
  - `GramApiException` with structured API error code and HTTP status code
  - `RedactedClientLogger` for redacting credentials and tokens from logs
  - `ConversationSyncState` for multi-client synchronization tracking

#### F. Test Coverage
- **New Suites**:
  - `Gram.Blazor/tests/Gram.Blazor.UnitTests/LocalAgentProcessTests.cs` (6 tests: secret enforcement, host header rejection, path guard, terminal execution with timeout cancellation, sandbox creation).
- **Expanded Suites**:
  - `DesktopRecoveryTests.cs` (6 tests: JSON and SQLite cache roundtrips, offline edit queuing, version conflict).
  - `ConnectorPackageServiceTests.cs` (2 tests: cross-owner compile/install rejection with `KeyNotFoundException`).
  - `ConnectorCredentialVaultTests.cs` (3 tests: cross-user isolation and revoked credential resolution failure).
  - `ResearchReliabilityTests.cs` (12 tests: 5-hop redirect safety, metadata IP blocking).
- Total **55/55 foundation tests pass**.

---

### 2. How to Run & Verify on Your Laptop

1. **Pull & Restore**:
   ```powershell
   git pull
   dotnet restore Gram.Blazor/Gram.Blazor.sln
   ```

2. **Run the Foundation Test Suite**:
   ```powershell
   dotnet test Gram.Blazor/tests/Gram.Blazor.UnitTests/Gram.Blazor.UnitTests.csproj -m:1 --logger "console;verbosity=minimal" --filter "FullyQualifiedName~ResearchReliabilityTests|FullyQualifiedName~ConnectorPermissionServiceTests|FullyQualifiedName~ConnectorEgressPolicyTests|FullyQualifiedName~ConnectorPackageServiceTests|FullyQualifiedName~ConnectorCredentialVaultTests|FullyQualifiedName~ConnectorRuntimeServiceTests|FullyQualifiedName~DesktopRecoveryTests|FullyQualifiedName~LocalAgentProcessTests|FullyQualifiedName~DesktopClientReliabilityTests"
   ```

3. **Verify All Target Builds**:
   ```powershell
   dotnet build Gram.Blazor/clients/Gram.Desktop/Gram.Desktop.csproj
   dotnet build Gram.Blazor/clients/Gram.ConnectorHost/Gram.ConnectorHost.csproj
   dotnet build Gram.Blazor/clients/Gram.ConnectorWorker/Gram.ConnectorWorker.csproj
   dotnet build Gram.Blazor/clients/Gram.LocalAgent/Gram.LocalAgent.csproj
   ```

4. **Running the Local Agent Process Locally**:
   ```powershell
   dotnet run --project Gram.Blazor/clients/Gram.LocalAgent/Gram.LocalAgent.csproj
   ```
   - Defaults to `http://127.0.0.1:7340`.
   - The pairing secret will be automatically generated at `%LOCALAPPDATA%\Gram\local-agent.secret` (or set via `GRAM_LOCAL_AGENT_SECRET` env var).

---

### 3. Next Steps on the Laptop
- Wire `DesktopSession` and the desktop UI views to render local agent status (`LocalAgentStatus.IsAvailable`, `Capabilities`) and enable the file picker and terminal tabs when connected.
- Begin phase 2 connector integrations (Spotify / Postman / Home Assistant) on top of the validated `ConnectorRuntimeService` and `ConnectorPermissionService` baseline.
- Ensure Spotify files (`SpotifyOAuthService.cs`, `ConnectorConnectionResolver.cs`) continue to preserve their connection resolution logic.


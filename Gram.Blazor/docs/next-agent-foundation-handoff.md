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

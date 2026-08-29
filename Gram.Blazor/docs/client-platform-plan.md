# Gram Desktop and Mobile Client Plan

## Direction

Desktop and mobile are native clients of the same Gram backend. Neither client connects directly to PostgreSQL.

```text
Gram Web       ─┐
Gram Desktop   ─┼─ REST + SSE ──> Gram Backend ──> PostgreSQL
Gram Mobile    ─┘                    │
                                      └── Desktop Local Agent (desktop only)
```

The backend remains the source of truth for synchronized conversations, runs, events, models, providers, and server-owned artifacts. Local-only capabilities remain local to desktop.

## Proposed repository layout

```text
clients/
  Gram.Client.Contracts/     Shared DTOs and serialization rules
  Gram.Client.Core/          REST/SSE client, auth refresh, retry, sync state
  Gram.Desktop/              Native desktop shell and local-agent integration
  Gram.Mobile/               Mobile shell with reduced capabilities
local-agent/
  Gram.LocalAgent/           Localhost-only privileged capability service
tests/
  Gram.Client.ContractTests/
  Gram.Client.IntegrationTests/
```

The current `Shared/BackendContracts.cs` and `Shared/BackendClient.cs` are the initial source for the contracts/core projects. They should be moved or linked into frontend-neutral projects once the first native shell is scaffolded.

## Shared client core

Build this before either UI shell:

- typed conversation, run, artifact, model, and provider APIs;
- SSE subscription with sequence replay;
- access-token injection and refresh handling;
- normalized API errors;
- exponential retry for safe reads and reconnects;
- offline queue for conversation edits;
- version-conflict state requiring explicit refresh/merge;
- structured client logging with secrets redacted.

Acceptance criteria:

- A client can sign in, list conversations, start a run, reconnect to its events, cancel it, and retrieve its result.
- A network interruption does not duplicate messages or lose events.
- A stale conversation update surfaces a conflict instead of overwriting newer data.

## Desktop milestones

### D1 — Shell and backend connection

- Choose the native framework for the target OS matrix.
- Add navigation, conversation list, chat view, settings, and run progress UI.
- Use `GramBackendClient` exclusively for backend data.
- Add secure OS credential storage for refresh tokens.

### D2 — Native capabilities

- Pair with the localhost local agent using an installation secret.
- Add native file picker.
- Add terminal capability UI with explicit command scope and cancellation.
- Add sandbox creation/status/diff views.
- Add performance-monitoring views.

### D3 — Offline and recovery

- Local SQLite cache for recent conversations and pending edits.
- Resume active runs after restart using run ID and sequence number.
- Show backend unavailable state while local-only capabilities remain available.

## Mobile milestones

### M1 — Focused chat client

- Sign-in and token refresh.
- Conversation list and detail.
- Chat composer with streamed responses.
- Run cancellation and reconnect.
- Artifact viewing.

### M2 — Mobile synchronization

- Offline read cache.
- Queued edits with version conflict UI.
- Push/deep-link handling for completed runs where supported.

### M3 — Capability restrictions

Mobile must not expose desktop filesystem, terminal, sandbox, computer-control, or performance-monitoring operations. The backend and local agent must reject those requests even if a modified client attempts them.

## Local agent protocol

The local agent is a separate desktop process:

- binds to localhost only;
- requires a per-installation secret;
- exposes named capabilities, never generic shell/filesystem endpoints;
- validates client identity, run ownership, path scope, sandbox ownership, network policy, and resource budgets;
- emits capability events with sequence numbers;
- kills process trees on cancellation;
- remains usable for local-only work when the hosted backend is unavailable.

## Authentication and sync

Implement before native UI beta:

- device authorization endpoint;
- short-lived access token;
- refresh token rotation;
- device/session listing and revocation;
- backend ownership checks for conversations, runs, events, and artifacts;
- atomic conversation version updates;
- explicit `409 Conflict` responses.

## Test gates

### Contract gate

- JSON serialization compatibility.
- REST error normalization.
- SSE ordering, replay, and reconnect.
- Access-token refresh.

### Cross-client gate

- Start on web, observe on desktop/mobile.
- Start on desktop, observe on web/mobile.
- Reconnect after interruption.
- Detect concurrent edit conflicts.
- Retrieve the same artifact on every client.

### Security gate

- Unauthenticated requests rejected.
- Cross-user IDs rejected.
- Mobile local-agent calls rejected.
- Local-agent path escape rejected.
- Arbitrary shell endpoint unavailable.
- Refresh-token rotation and revocation verified.

## Recommended implementation order

1. Extract contracts/core client into frontend-neutral projects.
2. Implement device auth and backend ownership checks.
3. Implement client-core retry, refresh, offline cache, and conflict handling.
4. Scaffold the desktop shell.
5. Build and secure the local agent.
6. Scaffold the mobile shell with the reduced capability set.
7. Run cross-client integration tests against a PostgreSQL staging instance.
8. Release desktop/mobile behind feature flags and staged rollout.

## Decisions still required

- Desktop framework and supported operating systems.
- Mobile framework and minimum OS versions.
- Whether mobile needs push notifications in the first release.
- Whether desktop offline mode should run a local backend or only cache client data.
- Hosted backend deployment topology and PostgreSQL backup/restore policy.

# Gram Backend Architecture

## Purpose

Gram uses one backend contract surface for every client. The Blazor web UI currently runs the implementation in-process, while desktop and mobile clients can use the same versioned HTTP API without knowing about provider, storage, sandbox, or orchestration classes.

## Layers

```text
Web / Desktop / Mobile
        |
        |  shared contracts + REST/SSE
        v
API boundary: /api/v1
        |
        v
BackendFacade (in-process adapter)
        |
        +--> conversations and persistence
        +--> run tracking and cancellation
        +--> workflow router / agent orchestration
        +--> model gateway and search
        +--> artifacts and presentations
        +--> local-agent capability status
```

The facade is an adapter, not a second business-logic layer. It keeps the current Blazor application working while making the boundary explicit. A future `Gram.Api` project can host the same contracts and application services with minimal UI changes.

## Public API

- `GET/POST/PUT/DELETE /api/v1/conversations`
- `POST /api/v1/conversations/{id}/runs`
- `GET /api/v1/runs/{id}`
- `GET /api/v1/runs/{id}/events` (Server-Sent Events)
- `POST /api/v1/runs/{id}/cancel`
- `GET /api/v1/models` and `/api/v1/providers`
- `POST /api/v1/search`
- `GET /api/v1/artifacts/{id}` and `/content`
- `GET /api/v1/local-agent/status` and `/capabilities`

Run events are replayable by sequence number. Clients can reconnect with `Last-Event-ID` without duplicating already-rendered output. Run cancellation belongs to the run tracker, so disconnecting the initiating HTTP request does not accidentally cancel the work.

## Client strategy

- Web: cookies/session authentication and the in-process facade during migration.
- Desktop: REST/SSE client plus a paired local-agent bridge for explicitly approved filesystem, terminal, performance, and sandbox capabilities.
- Mobile: REST/SSE client; no direct machine access.

The local agent is capability-based. It must never expose a generic shell or unrestricted filesystem endpoint. Every operation carries a run/capability context and is checked against the sandbox and approval policy.

## Migration rule

New UI features should depend on the contract interfaces (`IConversationApi`, `IRunApi`, `IModelApi`, `ISearchApi`, and `IArtifactApi`). Provider classes, database services, and sandbox implementations stay behind the backend boundary. Once the desktop shell exists, the Blazor in-process registrations can be replaced with `GramBackendClient` without changing the UI’s domain types.

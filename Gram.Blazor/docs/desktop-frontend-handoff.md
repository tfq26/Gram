# Gram Desktop Frontend Handoff

## Purpose

Build the native Gram desktop experience against the existing backend. The frontend
must remain a client of the versioned REST/SSE contracts. It must not call model
providers, search providers, persistence, or sandbox processes directly.

The first backend-connected shell is in `clients/Gram.Desktop`. The UI layer should
consume `DesktopSession` from that project. `DesktopSession` owns loading state,
conversation selection, run creation, SSE streaming, cancellation, and normalized
errors.

## Current vertical slice

The desktop client already supports:

- `GET /api/v1/conversations`
- `GET /api/v1/models`
- `POST /api/v1/conversations`
- `GET /api/v1/conversations/{id}`
- `POST /api/v1/conversations/{id}/runs`
- `GET /api/v1/runs/{id}/events` using SSE
- `POST /api/v1/runs/{id}/cancel`

The shared client is `GramBackendClient`; the UI-facing coordinator is
`DesktopSession`.

## Required desktop screen

Create a three-region workspace:

1. Sidebar: conversation list, new conversation action, connection indicator.
2. Canvas: message history, streaming assistant response, composer, cancel action.
3. Infosheet: selected model, run status, timestamps, and error state.

Use the internal code names `Sidebar`, `Canvas`, `Infosheet`, and `Menubar` for
component names or view-model regions only. Do not display these names to users.

The Menubar should contain the app identity, backend connection state, model picker,
and settings entry. It should not contain provider API-key controls.

## Binding behavior

On startup:

```csharp
var http = new HttpClient { BaseAddress = new Uri(backendUrl) };
var backend = new GramBackendClient(http, accessToken);
var session = new DesktopSession(backend);
await session.LoadAsync();
```

Subscribe to `StateChanged` and re-render. Subscribe to `RunEventReceived` for
event-specific UI effects, but keep the visible transcript derived from the session
and conversation data.

Composer flow:

1. Disable send while `session.IsStreaming` is true.
2. Call `SendAsync(text, selectedModelId)`.
3. Render `DraftResponse` while events arrive.
4. Show a quiet cancel control while streaming.
5. Refresh the conversation from the session after completion.

Do not render raw tool-call payloads, provider request bodies, console output, or
internal event diagnostics in the normal chat surface. Tool activity should appear
only as a compact, non-interactive status state if the product later requires it.

## Model display

Use the model API response rather than hardcoding provider names. Current product
roles are:

- Gram Q: Gemini 3.1 Flash Lite
- Gram Q+: Muse Spark 1.3 Contributor through Vercel AI Gateway
- Gram Q++: Gemini compatibility role
- Ling Free: simple test/classification role

The frontend should display `ModelSummary.Name`, `Provider`, and `Description`, but
send only `ModelSummary.Id` back to the backend.

## Authentication boundary

Desktop authentication will use a short-lived access token and refresh token. Keep
tokens in the platform secure credential store when the native shell is added. Do
not put tokens in conversation state, logs, query strings, or UI diagnostics.

Until the token refresh flow is implemented, support `GRAM_ACCESS_TOKEN` for local
development only. A 401 should produce a sign-in/connection state, not a raw HTTP
exception screen.

## SSE rules

- Events are ordered by `RunEvent.Sequence`.
- Store the last received sequence per active run.
- Reconnect with `Last-Event-ID` after a dropped connection.
- Ignore duplicate events.
- Treat `text.delta` as append-only output.
- Treat `run.completed`, `run.failed`, and `run.cancelled` as terminal states.
- A reconnect must not clear already-rendered assistant text.

## Local agent boundary

Do not implement filesystem or terminal access in the backend client. Those features
will use a separate authenticated localhost Gram Agent bridge. The future UI may
show capability availability through `LocalAgentStatus`, but should gracefully hide
or disable local actions when the bridge is unavailable.

The mobile frontend must never receive or invoke these capabilities.

## Error and empty states

Implement polished states for:

- backend unavailable;
- unauthorized/expired session;
- empty conversation list;
- empty conversation;
- active run reconnecting;
- cancelled run;
- normalized API error;
- no models available.

Errors should be concise and actionable. Never show API keys, provider request
payloads, stack traces, or raw SSE lines to users.

## Acceptance checklist

- The desktop project builds independently with the web backend project reference.
- A user can create and select a conversation.
- A user can select a model and send a message.
- Assistant text streams into the Canvas.
- Cancel stops the active run and updates the UI.
- Reconnecting does not duplicate streamed text.
- A backend outage has a recoverable UI state.
- No provider-specific HTTP code exists in the frontend.
- No local filesystem or shell access is exposed by the frontend shell.

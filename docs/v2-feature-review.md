# Gram v2 UI feature review

This branch recreates the original first-commit Gram conversation shell in the Gram v2 TypeScript UI. The carbon palette, three-pane proportions, top tabs, previous-chat rail, centered conversation, bottom composer, and Settings / Artifacts / Diff sidecar follow the original layout.

The static assets copied from commit `1dcfe6e` are served unchanged from `src/server/classic-assets`: `app.css`, `shadcn.css`, the Gram logo, provider logos, icons, and `audio.js`. `v2-adapter.css` contains the styles needed to bind v2 run output and virtual overlay diffs into the original shell.

The browser UI and local web server are implemented in TypeScript. The existing Rust workspace and `gram-core-rs` crate remain in the v2 project; this UI port does not add a separate Rust web server.

The chat route has no right-side pane. Settings is a separate `/settings` page with the original grouped navigation.

## Ported into the v2 shell

- Previous chats map to v2 run checkpoints.
- New chat starts a fresh v2 session.
- Model selection uses the current v2 model registry.
- Settings sidecar includes title, system prompt, rules, permission choice, attached text files, and clear chat.
- Ask mode opens a run approval step. Safe mode adds a virtual-overlay constraint to the run prompt.
- Voice input uses the original Gram audio visualizer and browser speech-recognition asset.
- Context and token telemetry is retained with estimated token counts because the v2 runtime does not report provider usage.
- Artifacts lists files staged in the v2 virtual overlay.
- Diff uses the v2 overlay diff endpoint and Monaco.

## V2-only UI surfaces deferred for review

These controls are commented out or omitted from the classic shell. Their runtime/API code remains available.

- Live Preview and device-size controls.
- Workspace file browser and read-only source editor.
- Direct commit of virtual changes to host files.
- Terminal and activity log drawer.
- Chat sidecar tabs for Artifacts and Diff; those are deferred rather than placed at the right of the chat.
- Gram Connect device list and AHP presence controls.
- Sandbox Doctor diagnostics panel.
- Agent execution-plan and checkpoint inspector in the conversation.
- Semantic graph, Code Mode, hypothesis racing, runtime probes, semantic blame, and other agent-tool dashboards.
- Context folding and workspace geometry inspector surfaces.

## Follow-up decisions

- Whether to restore a standalone Artifacts store alongside the overlay file list.
- Whether to port spoken assistant output and persistent attachment storage from the original Blazor app.
- Whether Safe should become a server-enforced permission policy.
- Whether V2-only panels should return as sidecar tabs or move to separate destinations.
- Whether to restore standalone Toolbox and account-management pages.


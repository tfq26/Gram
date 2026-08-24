# Gram · Multi-Purpose Reusable Sidecar & Controlled Bottom Drawer Architecture

## 1. Unified Multi-Purpose Right Sheet (Sidecar)
The right pane (`<aside class="pane pane-right">`) becomes a versatile, mode-switchable Sidecar capable of seamlessly transitioning between views without cluttering the chat flow:

### Supported Sidecar Modes:
1. **`Settings` (Chat Settings & Telemetry)**:
   - Real-time token usage telemetry & context utilization meter.
   - Conversation title override, Model switcher, Permission modes (`Autonomous`, `Safe`, `Ask`).
   - Per-chat system prompt and custom instruction rules.
2. **`Artifact` (Interactive Code / Markdown / Live Preview)**:
   - Full code view with syntax highlighting, copy button, and line numbers.
   - Interactive live renderer for HTML/SVG/UI snippets and Markdown reports.
   - Direct download and "Apply to Workspace" action triggers.
3. **`GitDiff` (Change & Diff Inspection)**:
   - Unified & split colorized diff view (additions in green, deletions in red).
   - "Accept Change" / "Reject Change" controls for agent-proposed file edits.

---

## 2. Controlled Bottom Drawer (Focused Workspace Drawer)
Rather than an uncontrolled terminal or noisy live log stream, the collapsible bottom drawer is dedicated to controlled, structured workspace panels:

### Curated Bottom Drawer Tabs:
1. **Output & Test Runner**:
   - Structured build status (`dotnet build` summaries) and test pass/fail breakdown cards.
2. **LSP Diagnostics & Errors**:
   - Clean list of compiler errors, warnings, and code analysis suggestions with file/line links.
3. **Task & Agent Timeline**:
   - Step-by-step chronological progress checklist of multi-step agent iterations.

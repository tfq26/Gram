# Gram · Feature Roadmap & Verification Queue

## 1. Features Ready for Verification (Current Turn)
The following features were just implemented and are ready for interactive testing:

| Feature | Key Trigger / Action | Expected Result |
| :--- | :--- | :--- |
| **Context Mentions** | Type `@` in composer input (e.g. `@git:diff`, `@web:`, `@toolbox`, `@Program.cs`) | Opens autocomplete dropdown popup; selecting an item inserts the mention into the draft. |
| **Token & Context Telemetry** | Click the **Chat Settings** button (sliders icon in top-right) | Displays Prompt Tokens, Completion Tokens, Total Tokens, Latency, and a live Context Window Progress Bar. |
| **ShadCn `<Avatar />`** | View message thread & sidebar profile footer | User messages display the user's avatar centered on the right edge with compact padding; model responses display vendor logo icons on the left. |
| **Live Web Search & Fetch** | Prompt model to look up online documentation or search query | Model autonomously invokes `search_web` / `fetch_web_content` and extracts live web results. |
| **Gram Toolbox Engine** | Ask model to inspect toolbox tools or run sandbox diagnostics | Model leverages `toolbox_list_tools` and `toolbox_execute_tool` to interact with sandboxes, AST parsers, and LSP bridges. |

---

## 2. Next Up in Queue (Prioritized Feature Roadmap)

### 🥇 Priority 1: Interactive Artifact & Sidecar Preview Panel
- **What**: When the model generates a complete code file, UI component (Blazor, HTML/CSS, React), or Markdown report, display a clickable card in chat that opens a full **Artifact Sidecar Panel** on the right.
- **Why**: Eliminates clutter from long code blocks in chat and enables side-by-side editing, syntax highlighting, and live rendering.

### 🥈 Priority 2: Interactive Diff & Change Inspection Viewer
- **What**: When the model executes edits (`edit_file` / `write_file`), render an inline or side-by-side colorized diff view (red deletions / green additions) with **Accept Change** and **Reject Change** buttons.
- **Why**: Essential for pair-programming safety so developers can review code before it touches their working tree.

### 🥉 Priority 3: Integrated Multi-Tab Terminal & Log Drawer
- **What**: A collapsible drawer at the bottom of the workspace providing a live pseudo-terminal stream and real-time execution log watcher for commands run by the agent.
- **Why**: Allows users to watch `dotnet test`, `npm run build`, and container execution in real time without opening an external terminal window.

### 4. Human-in-the-Loop Approval Modal for Risky Tools
- **What**: Configurable safety prompt before executing destructive terminal commands (`rm`, `git push --force`, `drop database`).
- **Why**: Complements the Security Permission Mode (`Full Autonomous` vs `Safe Auto-Approve` vs `Ask Always`).

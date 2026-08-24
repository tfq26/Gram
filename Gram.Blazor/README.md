# Gram — Autonomous Agentic Coding Studio & VCS Orchestrator

> **Gram** is a modern, high-performance, conversation-first pair-programming harness and agentic workspace orchestrator built on .NET 9 Blazor and ShadCn principles.

---

## 1. What is Gram?

Gram is an **Autonomous Agentic Coding Studio**. It combines state-of-the-art Large Language Models (Claude 3.7 Sonnet, GPT-4o, DeepSeek V3/R1, Gemini 2.5 Flash, Qwen 2.5, and local Ollama instances) with deep workspace tools, sandbox execution engines, and a dedicated background version control custodian (**Git Gram**).

Unlike simple chat web interfaces or heavy browser-based IDEs, Gram provides an autonomous pair-programming environment where the conversation remains clean and focused, while code artifacts, diffs, telemetry, and background git tasks slide into view seamlessly.

---

## 2. Why is Gram Needed? (The Problem with Existing Tools)

1. **The "Mini-IDE" Trap**: Many web coding assistants attempt to recreate VS Code in the browser with cluttered split editors, permanent terminal windows, and complex file trees. This creates excessive cognitive overhead and duplicates desktop IDEs poorly.
2. **Uncontrolled Git Drift & Messy Commits**: Typical coding agents dump large blocks of code without managing version control hygiene, leading to broken repositories and uncommitted chaos.
3. **Noisy Terminal & Log Dumps**: Most agent harnesses overwhelm developers with hundreds of lines of raw compiler or tool output instead of curated, actionable summaries.
4. **Fragile Context Management**: Developers struggle with token limits and context window degradation without real-time telemetry or structured context compaction.

---

## 3. How is Gram Different?

| Capability | Generic Chat / Codex | Cursor / Windsurf | **Gram** |
| :--- | :--- | :--- | :--- |
| **Interface Philosophy** | Basic markdown chat | Full Desktop Editor clone | **Conversation-First Agentic Studio** |
| **Git Management** | Manual user commits | Git diff view in editor | **Git Gram**: Autonomous background VCS custodian with safety guardrails |
| **Inspection Workspace** | Static code snippets | In-editor buffers | **Reusable Sidecar**: Settings, Artifacts, and Diffs on demand |
| **Toolbox & Sandboxing** | Proprietary/Cloud-only | Local file edits | **Gram Toolbox**: Container Sandbox, AST parsing, LSP bridge, MCP servers |
| **Model Freedom** | Single vendor lock-in | Subscription tiers | **Any Model Anywhere**: OpenRouter, Ollama, DeepSeek, Anthropic, OpenAI |
| **Design Language** | Generic UI | Electron editor theme | **Custom ShadCn Design System** (Dark/Light/Dim, 0 emojis, high density) |

---

## 4. Core Features

### 💬 Conversation-First Workspace
- **Distraction-Free Chat Canvas**: Centered, responsive chat stream with custom user avatar alignment, vendor-specific model provider badges, and inline expandable tool results.
- **Real-Time Streaming**: Low-latency token streaming with reasoning token support (`<thinking>` tags rendered cleanly in dedicated thought accordions).
- **Context Mentions (`@`)**: Type `@` to dynamically attach files, live web searches, or documentation into prompts.

### 🌿 Git Gram (Autonomous VCS Specialist)
- **Single-Repo Custodian**: Dedicated background agent operating exclusively on the active repository.
- **Asynchronous Task Queue**: Runs branch creation, semantic conventional commits (`feat:`, `fix:`, `chore:`), status scans, and staging safely in the background.
- **Strict Safety Guardrails**: Hard blocks destructive commands (`reset --hard`, `clean -fd`, `push --force`, `branch -D`) without explicit user permission.
- **Interactive Diff Inspection**: View line-by-line colored diffs, stage hunks, accept changes, or discard unwanted edits with one click.
- **Sonner Toast Notifications**: Instant, unobtrusive toast feedback when commits, branches, or file synchronizations complete.

### 📄 Reusable Multi-Purpose Sidecar
- **Settings Mode**: Real-time context window gauge, token usage metrics, latency tracker, single-word permission modes (**Autonomous**, **Safe**, **Ask**), and system prompts.
- **Artifacts Mode**: Interactive code viewer with live HTML/SVG preview rendering and 1-click "Apply to Workspace".
- **Diff Mode**: Colorized workspace diffs with additions, deletions, and hunk headers.
- **Git Gram Mode**: Live background task queue, repository health status, and full audit command history.

### 🧰 The Gram Toolbox
- **Container Sandbox**: Isolated Docker/Podman runtime for safe script and package execution without host risks.
- **Coder CDE Bridge**: Provision and orchestrate remote cloud development environments.
- **Tree-Sitter Semantic AST**: AST-level code symbol navigation and semantic parsing.
- **LSP Bridge**: Real-time compiler diagnostics and type checking.
- **Model Context Protocol (MCP)**: Native stdio and SSE connections to community MCP servers (SQLite, GitHub, PostgreSQL, Figma, Jira).

---

## 5. Technology Stack

- **Framework**: .NET 9 ASP.NET Core Blazor (Interactive Server Components)
- **Language**: C# 13
- **Frontend / Styling**: Vanilla CSS with custom ShadCn design tokens, zero external heavy component libraries, and strict zero-emoji UI design rules.
- **VCS Engine**: Git CLI process wrapper with asynchronous streams and strict safety filters.
- **Persistence**: SQLite and local storage service with atomic JSON file persistence.
- **Security**: Granular execution permissions, scoped sandbox boundaries, and encrypted credential storage.

---

## 6. Hosting & Deployment Environments

Gram is designed to run everywhere from local workstations to enterprise private clouds:

1. **Local Workstation (Developer Mode)**:
   - Run directly via `dotnet run` on Windows, macOS, or Linux.
   - Connects to local Ollama instances for 100% offline, private AI development.

2. **Self-Hosted Docker / Podman Container**:
   - Packaged as a lightweight multi-arch container image.
   - Mount local project directories directly into the container workspace.

3. **Cloud & Enterprise CDEs (Coder.com / Kubernetes)**:
   - Deploy as a web-accessible workspace sidecar inside Coder, Gitpod, or Kubernetes dev pods.
   - Connects to enterprise OpenRouter, Azure OpenAI, or self-hosted vLLM/TGI inference endpoints.

---

## 7. Getting Started

### Prerequisites
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Git](https://git-scm.com/)

### Quick Start
```bash
# Clone the repository
git clone https://github.com/your-org/gram.blazor.git
cd gram.blazor

# Build the project
dotnet build

# Launch Gram Studio
dotnet run
```
Open your browser at `http://localhost:7337` to start using Gram.

---

## 8. License

MIT License — Built with care by the Gram Team.

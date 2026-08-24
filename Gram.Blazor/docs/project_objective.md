# Gram · Project Objective & Architectural Overview

## 1. Core Mission
**Gram** is an autonomous, developer-first AI workspace and pair-programming system built with **Blazor & .NET 9**. 

Its objective is to provide a fast, privacy-focused, and modular environment where developers can chat with cutting-edge AI models, execute autonomous coding loops, inspect codebases, and interact with external development tools seamlessly.

---

## 2. Core Pillars

```
                      ┌─────────────────────────────────┐
                      │              GRAM               │
                      │  (Core Blazor Autonomous UI)    │
                      └──────────────┬──────────────────┘
                                     │
         ┌───────────────────────────┼───────────────────────────┐
         │                           │                           │
         ▼                           ▼                           ▼
┌──────────────────┐       ┌──────────────────┐       ┌──────────────────┐
│ Intelligence     │       │ Multi-Provider   │       │ Gram Toolbox     │
│ & Chat UX        │       │ Architecture     │       │ (Modular Engine) │
├──────────────────┤       ├──────────────────┤       ├──────────────────┤
│• Voice & Chime   │       │• OpenRouter      │       │• Container Sandbox│
│• Frameless Flow  │       │• Anthropic/OpenAI│       │• Coder CDE Cloud │
│• Copy & Retry    │       │• Ollama (Local)  │       │• Tree-Sitter AST │
│• Error Panels    │       │• AES-256 Keys    │       │• LSP Bridge      │
│• Web Search      │       │• Health Probing  │       │• MCP Connectors  │
└──────────────────┘       └──────────────────┘       └──────────────────┘
```

---

### A. Dual-Offering Strategy (Core Gram vs. Gram Toolbox)
1. **Core Gram (Base Experience)**:
   - Ultra-clean, distraction-free conversational AI workspace.
   - Live web search and web documentation fetching (`search_web`, `fetch_web_content`).
   - Voice transcription with hands-free wake word detection and chime audio feedback.
   - Remote Git integration (GitHub, GitLab, Forgejo, Bitbucket) for cross-repository commits and PR generation.

2. **Gram Toolbox (Advanced Autonomous Agentic Layer)**:
   - **Embedded Sandbox**: Isolated Docker/Podman execution preventing local host machine risk.
   - **Coder CDE Integration**: Cloud developer workspace provisioning and remote agent work.
   - **Tree-Sitter Semantic AST**: Concrete syntax tree parsing for deep code symbol intelligence.
   - **Language Server Protocol (LSP)**: Real-time compiler diagnostics (Roslyn, rust-analyzer, vtsls).
   - **Model Context Protocol (MCP)**: Native connection to community MCP servers (SQLite, Jira, Figma, etc.).

---

### B. Security & Privacy
- **Client-Side Encryption**: API keys are encrypted at rest with AES-256 (`secure_keys.dat`) keyed to machine/user hashes.
- **Local First**: Full support for local models via Ollama and local SQLite session transcripts.
- **Audit Trails**: Detailed JSONL trajectory logging (`transcript.jsonl`) for full execution reproducibility.

---

### C. Modern Developer UX
- **Frameless Stream**: Assistant markdown output renders as a natural document flow rather than restrictive chat bubbles.
- **Diagnostics on Demand**: Failures display user-friendly error banners with collapsible deep diagnostics and instant background retry.
- **Dynamic Model Switching**: Animated model picker with provider badges, context lengths, and favorites.

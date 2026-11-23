# Technical Audit: Gram Codebase

**Date:** 2026-10-05  
**Auditor:** Senior Staff Engineer  
**Scope:** Model-Agnostic Agentic Coding Harness (OpenAI Codex Alternative)  
**Status:** Phase 0 (Product Definition & Inventory Completed)

---

## Executive Verdict (Pre-Flight)

> Gram is currently a **talented prototype trapped inside a tangled web of unmerged experiments**. 
> While it contains sophisticated, high-effort implementations (a 1,870-line multi-step agent orchestrator, token quota guards, Git worktree isolation, local agent daemon pairing, and AST symbol extraction), the project suffers from severe identity crises and architectural fragmentation. 
> 
> Projects named "Desktop" are actually headless CLIs; desktop GUIs are actually MacCatalyst MAUI shells cross-referencing build DLLs via hardcoded file paths; the Python context engine is disconnected from the main launcher; and high-priority coding agent loops compete with voice companion avatars, Spotify OAuth plugins, and presentation slide generators. 
> 
> It is not yet a coherent product, but the core foundation for a real Codex alternative exists beneath the clutter if aggressively consolidated.

---

## Phase 0: Product Definition & Inventory

### 1. Component & Systems Map

The codebase is organized as a distributed, multi-runtime repository primarily built on **.NET 10 (C#)** with a secondary **Python 3.12+** sidecar and **Bash** orchestration.

| Component / Subsystem | Language & Framework | Runtime Process / Port | Role & Purpose | Inter-Process & System Connections | Evidence / Key Files |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **`Gram.Server`** | C# / ASP.NET Core Minimal API (.NET 10) | Host process listening on `0.0.0.0:7337` (PID spawned by `launch.sh`) | Central backend REST API, SSE event hub, auth gateway, and Blazor InteractiveServer circuit host. | Dispatches runs to `AgentOrchestrator`, streams events over `/runs/{id}/events` via `RunManager`, persists state to SQLite/Postgres. | [Gram.Server.csproj](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Gram.Server/Gram.Server.csproj#L1-L13)<br>[Program.cs](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Program.cs#L77-L120) |
| **`Gram.Blazor`** (Shared Core) | C# / Razor Class Library (.NET 10) | Runs in-process inside `Gram.Server` (and partially linked in `Gram.Maui`) | Core domain model, state machines, agent execution loop, tool implementations, and UI components (`Workspace.razor`). | Calls external LLM APIs (OpenAI, Gemini, Groq, OpenRouter) via raw `HttpClient`; communicates with `Gram.LocalAgent` via HTTP. | [AgentOrchestrator.cs](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L27-L85)<br>[ModelService.cs](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/ModelService.cs#L1160-L1200)<br>[ToolExecutor.cs](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/ToolExecutor.cs) |
| **`Gram.Desktop`** *(Misnamed)* | C# (.NET 10 Console CLI) | Ephemeral process; run on-demand via `./launch.sh --cli` | **A headless CLI runner** (`Gram CLI v2.2.0`), NOT a desktop GUI app. Stripped of GUI code via `.csproj`. | Connects via HTTP/SSE to `Gram.Server:7337` to start runs, stream deltas, and print ANSI color output to `stdout`. | [Gram.Desktop.csproj](file:///Users/taufeeqali/projects/gram/Gram.Blazor/clients/Gram.Desktop/Gram.Desktop.csproj#L22-L26)<br>[Program.cs](file:///Users/taufeeqali/projects/gram/Gram.Blazor/clients/Gram.Desktop/Program.cs#L12-L35) |
| **`Gram.LocalAgent`** | C# / ASP.NET Core Minimal API (.NET 10) | Background loopback daemon listening on `127.0.0.1:7340` (PID spawned by `launch.sh`) | Secure host daemon executing workspace-scoped native OS commands (`terminal.run`) and safe file reads (`files.pick`). | Secured via loopback host enforcement and bearer token `X-Local-Agent-Secret`. Invoked by `Gram.Server` when `allowLocalCapabilities=true`. | [Program.cs](file:///Users/taufeeqali/projects/gram/Gram.Blazor/clients/Gram.LocalAgent/Program.cs#L36-L94)<br>[LocalAgentContracts.cs](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/LocalAgentContracts.cs) |
| **`Gram.Maui`** | C# / .NET 10 MAUI (MacCatalyst / WinUI) | Native macOS GUI application (`Gram.app`), runs BlazorWebView | The actual native desktop application wrapper for macOS and Windows. | Renders Blazor UI inside a native webview; links directly to compiled `Gram.Blazor.dll` via hardcoded build output path. | [Gram.Maui.csproj](file:///Users/taufeeqali/projects/gram/Gram.Blazor/clients/Gram.Maui/Gram.Maui.csproj#L4-L34)<br>[MainPage.xaml.cs](file:///Users/taufeeqali/projects/gram/Gram.Blazor/clients/Gram.Maui/MainPage.xaml.cs) |
| **`Gram.Mobile` / `Gram.iOS`** | C# / .NET 10 Class Lib / MAUI | Class library compiled into `Gram.Blazor`; `Gram.iOS` is standalone | Incomplete mobile/tablet UI layout experiments. | Embedded in `Gram.Blazor.csproj` via `<Compile Include="clients/Gram.Mobile/*.cs" />`. | [Gram.Blazor.csproj](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Gram.Blazor.csproj#L34-L35) |
| **`Gram.ConnectorHost` & `Gram.ConnectorWorker`** | C# (.NET 10 Console) | Standalone external worker processes | Experimental out-of-process isolation for third-party tools/plugins (Spotify, Desktop Use, etc.). | Communicates over local transport with `ConnectorRuntimeService.cs`. | [ConnectorRuntimeService.cs](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/ConnectorRuntimeService.cs) |
| **`GramBox.Client` & `GramBox.Contracts`** | C# (.NET 10 Class Libs) | In-process DTOs and client wrappers | Contract definitions for containerized/isolated sandboxes (Docker / microVMs). | Consumed by `Gram.Blazor` and `Gram.LocalAgent`. | [GramBox.Contracts.csproj](file:///Users/taufeeqali/projects/gram/Gram.Blazor/clients/GramBox.Contracts/GramBox.Contracts.csproj) |
| **`engine/`** (Agent Sidecar) | Python 3.12+ / FastAPI / Tree-sitter / Strands | Standalone daemon on `127.0.0.1:8787` (managed via `scripts/launch-engine.sh`) | Context compression (Headroom CCR), Tree-sitter AST symbol extraction, Playwright web browser verification, and memory extraction. | Polled by `GramEngineClient.cs` from `Gram.Blazor`. If not running, `GramEngineClient` silently falls back to null. **Not started by `launch.sh`**. | [server.py](file:///Users/taufeeqali/projects/gram/engine/server.py#L25-L60)<br>[GramEngineClient.cs](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/GramEngineClient.cs#L86-L125) |
| **`launch.sh`** | Bash | Shell entry point | Unified bootstrapper that checks prerequisites, starts `Gram.Server`, starts `Gram.LocalAgent`, and opens the browser/desktop app. | Starts ports 7337 and 7340, sets trap cleanup routines on exit. | [launch.sh](file:///Users/taufeeqali/projects/gram/launch.sh#L12-L96) |

---

### 2. What This Product Actually Does Today (3-Sentence Truth)

1. Gram provides a three-pane web and macOS desktop interface (Blazor Server) where users can chat with OpenAI, Groq, Gemini, or Vercel AI Gateway models over Server-Sent Events with streaming token metrics and tool status indicators.
2. When permitted by the user, the agent loop executes shell commands, inspects files, and applies diffs/patches against local directories, either directly in-process or delegated through a localhost daemon (`Gram.LocalAgent`) on port 7340.
3. Alongside this coding capability, the repository houses unintegrated or half-integrated peripheral experiments—including an autonomous Python AST/CCR engine that is not wired into the main launcher, an out-of-process Spotify connector system, and a floating companion mascot avatar.

---

### 3. Core User Loop Analysis

**Expected Codex Loop:**  
`User Request` $\rightarrow$ `Workspace Context / AST Discovery` $\rightarrow$ `Agent Reasoning & Multi-step Plan` $\rightarrow$ `Precise Code Edits / Patches` $\rightarrow$ `Verification / Test Execution` $\rightarrow$ `Self-Correction on Failure` $\rightarrow$ `Diff Presentation & User Approval`.

#### Is it implemented end-to-end?
**Verdict: Implemented only in pieces with fragile bridges.**

- **User Request & Context Ingestion:** **Partially Implemented.**  
  `ComposerDock.razor` captures prompt text and attachments. It invokes `DesktopSession.SendAsync()` ([DesktopSession.cs:404](file:///Users/taufeeqali/projects/gram/Gram.Blazor/clients/Gram.Desktop/DesktopSession.cs#L404)), which packages a `SendMessageRequest` and dispatches it to `/api/v1/conversations/{id}/runs`. However, workspace indexing via Tree-sitter AST ([ast_symbol_extractor.py](file:///Users/taufeeqali/projects/gram/engine/ast_symbol_extractor.py)) is bypassed unless the user manually starts the separate Python engine daemon via `launch-engine.sh`. By default, only a shallow file tree is scraped.
- **Agent Reasoning & Plan:** **Implemented.**  
  `AgentOrchestrator.RunAgentLoopAsync` ([AgentOrchestrator.cs:71](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L71)) manages up to 50 iterations, dispatches tool definitions, and streams content deltas, thinking blocks, and tool calls.
- **Code Edits & Tool Execution:** **Implemented with Dual Divergent Paths.**  
  Tools are executed either via in-process `ToolExecutor.cs` or proxied to `Gram.LocalAgent` via HTTP. File edits rely on regex search-and-replace (`patch_file`) or whole file rewrites (`write_file`), which are notoriously prone to hallucinations on large files.
- **Verification & Self-Correction:** **Fragile / Inconsistent.**  
  While `SelfHealingLoopCoordinator` exists ([AgentOrchestrator.cs:277](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L277)) and `CompilerDiagnosticParser.cs` exists, they are not triggered systematically across all workflows. In standard chat runs, if a bash command fails, the error message is simply fed back into the next prompt turn; there is no structured rollback or git worktree isolation unless the specific `GramBoxWorktreeManager` code path is activated.
- **Diff Presentation & User Review:** **Fragmented.**  
  `InteractiveDiffViewer.razor` exists, but file changes made via direct terminal commands (`sed`, `cat`, or build tools) bypass the diff viewer entirely and mutate the user's working tree in-place without staged confirmation.

---

### 4. Overlaps and Gaps

#### Major Duplications & Redundancies:
1. **Three Disjoint Model Calling Layers:**
   - `ModelService.StreamChatAsync` ([ModelService.cs:1160](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/ModelService.cs#L1160)): Implements streaming chat completions over HTTP for basic conversations and title generation, but has **no tool-calling support**.
   - `AgentOrchestrator.RunAgentLoopAsync` ([AgentOrchestrator.cs:280-450](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L280-L450)): Duplicates the entire HTTP client streaming pipeline, manually constructing OpenAI JSON schemas and parsing tool calls.
   - `ModelGateway.cs` ([ModelGateway.cs:11-30](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/ModelGateway.cs#L11-L30)): A 30-line interface that wraps `ModelService.StreamChatAsync`, completely ignored and unused by `AgentOrchestrator`.
2. **Duplicated File Viewers & Message Lists:**
   - `Gram.Blazor/Components/MessageList.razor` vs. `Gram.Blazor/clients/Gram.Desktop/Components/DesktopMessageList.razor`.
   - `Gram.Blazor/Components/CodeFileViewer.razor` vs. `Gram.Blazor/clients/Gram.Desktop/Components/DesktopEditorViewer.razor`.
3. **The "Desktop" Directory Identity Crisis:**
   - `Gram.Desktop/` contains full Blazor UI components and session services (`DesktopSession.cs`, `DesktopMessageList.razor`), but `Gram.Desktop.csproj` explicitly removes them ([Gram.Desktop.csproj:22-26](file:///Users/taufeeqali/projects/gram/Gram.Blazor/clients/Gram.Desktop/Gram.Desktop.csproj#L22-L26)). Instead, `Gram.Blazor.csproj` reaches across project folders to compile them ([Gram.Blazor.csproj:30-33](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Gram.Blazor.csproj#L30-L33)). `Gram.Desktop` is actually just a CLI runner.

#### Critical Gaps (Missing Pieces for a Codex Alternative):
1. **No Language Server Protocol (LSP) or In-Process AST Engine:**  
   Codebase understanding in the primary C# runtime relies on brute-force file scanning (`WorkspaceFileService.cs`). The Tree-sitter AST extractor is quarantined in `engine/ast_symbol_extractor.py` and is offline during standard `./launch.sh` runs.
2. **Missing Real Sandboxing on macOS/Linux:**  
   `GramBox.Contracts` defines sandbox interfaces, but `Gram.LocalAgent` executes commands directly on the host operating system via `Process.Start` ([NativeProcessRunner.cs](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/NativeProcessRunner.cs)). A malicious model prompt or hallucinated command (`rm -rf ~` or `curl ... | sh`) executes with full user permissions.
3. **Lack of Model-Agnostic Tool Normalization:**  
   `AgentOrchestrator` explicitly hardcodes OpenAI-style function definitions ([AgentOrchestrator.cs:250](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L250)). Models that do not natively support OpenAI tool-calling syntax (or require Anthropic `tools` JSON structure or Gemini `functionDeclarations`) must be routed through an intermediary proxy (like OpenRouter or Vercel Gateway) or they fail.
4. **No Automated Test / Regression Suite for Agent Tasks:**  
   While there are 560 unit tests for individual C# helpers, there is no SWE-bench style benchmark harness verifying whether a model can actually edit a file, run a build, inspect errors, and successfully recover.

---

### 5. Overall Coherence Rating & Justification

### **Coherence Score: 4.5 / 10**

#### Justification:
- **What stops it from being a 1-3:**  
  The code is not a non-functional mock. The .NET 10 compilation passes cleanly, all 560 unit tests pass, the SSE streaming architecture is functional, the pairing security between `Gram.Server` and `Gram.LocalAgent` is well-reasoned with timing-attack resistant cryptographic comparisons ([Gram.LocalAgent/Program.cs:82](file:///Users/taufeeqali/projects/gram/Gram.Blazor/clients/Gram.LocalAgent/Program.cs#L82)), and the UI layout is reactive and visually polished.
- **Why it cannot score above 4.5:**  
  It lacks architectural discipline. It is built like five independent hackathon projects that were merged into a single git repository without a unifying system boundary:
  1. It claims to be a desktop app, but the desktop project is a CLI, the MAUI app references DLLs via hardcoded relative paths, and the primary runner is a web server.
  2. It claims to be a model-agnostic coding assistant, but its agent loop is hardcoded to OpenAI's function schema with ad-hoc conditionals for Anthropic cache headers and reasoning effort parameters.
  3. Essential coding engine features (Tree-sitter AST symbols and context compression) are sequestered in an unlaunched Python daemon, while non-essential features (Spotify playback control, slide generators, avatar character animations) are integrated directly into the core server endpoints.

---

## Phase 1: System Design & Architecture

### 1. Architectural Topology & Process Boundaries

```mermaid
flowchart TB
    subgraph ClientLayer ["Client & Interaction Layer"]
        BrowserUI["Web Browser (Chrome/Safari)<br/>Blazor Server Circuit (WS / HTTP)"]
        MauiApp["Gram.Maui (MacCatalyst/WinUI)<br/>BlazorWebView Host"]
        CliRunner["Gram.Desktop (CLI v2.2.0)<br/>Terminal ANSI Streamer"]
    end

    subgraph ServerHost ["Process 1: Gram.Server (:7337)"]
        Kestrel["ASP.NET Core Kestrel Host"]
        BlazorCircuits["InteractiveServer Circuits<br/>(Workspace.razor, StateHasChanged)"]
        RestEndpoints["REST API (/api/v1/*)<br/>Runs, Models, Auth, Connectors"]
        SSEStreamer["SSE Event Streamer<br/>(/runs/{id}/events)"]
        
        subgraph OrchestrationSubsystem ["Agent Orchestration"]
            RunManager["RunManager<br/>(Execution coordinator)"]
            Tracker["AgentRunTracker<br/>(In-Memory Dicts: _runs, _events)"]
            AgentOrchestrator["AgentOrchestrator (1,870 LOC)<br/>Loop 1: Chat Completions<br/>Loop 2: OpenAI Responses"]
            QuotaRateLimiter["QuotaTracker & RateLimitCoordinator"]
        end

        subgraph ToolExecutionSubsystem ["Tool Execution Boundary"]
            ToolExecutor["ToolExecutor"]
            AgentTools["AgentTools (2,890 LOC / 51 tools)<br/>Giant switch(toolName)"]
            SecurityService["AgentSecurityService & EnvScrubber"]
            ProcessRunner["NativeProcessRunner (zsh -c / bash -c)"]
            GitWorktree["GramBoxWorktreeManager (git worktrees)"]
        end

        subgraph PersistenceSubsystem ["Persistence & Secrets"]
            SqliteStore["SqliteDatabaseService (~/.gram/gram.db)"]
            PostgresStore["PostgresDatabaseProvider (Optional)"]
            SecureKeys["secure_keys.dat (AES / Static Machine Salt)"]
        end
    end

    subgraph LocalDaemon ["Process 2: Gram.LocalAgent (:7340)"]
        LocalKestrel["ASP.NET Core Loopback Host"]
        LocalAuthFilter["LocalHost + Bearer Secret Validator"]
        WorkspaceRegistry["LocalAgentWorkspaceRegistry"]
        LocalTools["Local Endpoints<br/>files.pick, terminal.run, sandbox.create"]
    end

    subgraph SidecarDaemon ["Process 3: Python Engine (:8787) (OFFLINE BY DEFAULT)"]
        FastApi["FastAPI / Uvicorn Host"]
        AstExtractor["Tree-sitter AST Symbol Extractor"]
        HeadroomCCR["Context Compression & Retrieval (CCR)"]
        StrandsCoord["Strands Execution Coordinator"]
        PlaywrightVerifier["Playwright Web Verifier"]
    end

    subgraph ExternalServices ["External Cloud Services"]
        LLMProviders["LLM Providers (OpenAI, Groq, Gemini, OpenRouter)"]
        SpotifyAPI["Spotify OAuth API"]
    end

    %% Wiring
    BrowserUI -->|WebSocket / DOM Events| BlazorCircuits
    MauiApp -->|In-Process BlazorWebView| BlazorCircuits
    CliRunner -->|HTTP POST /api/v1/conversations/{id}/runs| RestEndpoints
    CliRunner -->|SSE /runs/{id}/events| SSEStreamer

    BlazorCircuits --> RunManager
    RestEndpoints --> RunManager
    RunManager --> Tracker
    RunManager --> AgentOrchestrator

    AgentOrchestrator -->|Raw HTTP JSON Streams| LLMProviders
    AgentOrchestrator --> QuotaRateLimiter
    AgentOrchestrator --> ToolExecutor

    ToolExecutor --> AgentTools
    AgentTools --> SecurityService
    AgentTools -->|Default: Direct Host Execution| ProcessRunner
    AgentTools -->|Optional Sandbox| GitWorktree
    AgentTools -->|When Configured: HTTP Proxy| LocalTools
    AgentTools -->|When Online: HTTP Proxy| FastApi

    RestEndpoints --> SpotifyAPI
    RunManager --> SqliteStore
    AgentOrchestrator --> SecureKeys
```

#### Where State Lives:
1. **Active Run Execution & Event Stream State:** **100% In-Memory (Ephemerally Held in Server RAM).**  
   Managed inside `AgentRunTracker` ([AgentRunTracking.cs:49-51](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentRunTracking.cs#L49-L51)) using `Dictionary<string, AgentRunSnapshot> _runs`, `_events`, and `_cancellations`.
2. **Chat History & Conversation Metadata:** **SQLite / PostgreSQL.**  
   Stored in `~/.gram/gram.db` via `SqliteDatabaseService.cs` (or Postgres if `DATABASE_URL` is set). Only synced upon run completion ([RunManager.cs:166-177](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/RunManager.cs#L166-L177)).
3. **UI Client Circuit State:** **ASP.NET Core Blazor Circuit Session.**  
   Held in memory per connected browser tab by `DesktopSession.cs`.
4. **API Keys & Credentials:** **Flat Encrypted File + Environment Variables.**  
   Stored in `~/.gram/secure_keys.dat` or unencrypted `.env` files.
5. **AST Symbol Indices & Compressed Output:** **Python Process Memory (Engine Cache).**  
   Stored in `ccr_cache` ([engine/server.py:31](file:///Users/taufeeqali/projects/gram/engine/server.py#L31)).

---

### 2. Separation of Concerns Evaluation

| Concern | Implementation Location | Health Status | Architectural Critique |
| :--- | :--- | :--- | :--- |
| **UI Presentation** | `Workspace.razor`, `DesktopCanvas.razor`, `ComposerDock.razor` | **Works but Fragile** | Leaks state management directly into Razor code. `Workspace.razor` acts as a 509-line god component binding platform flags, tabs, settings, audio, and companion tasks. |
| **Orchestration Loop** | `AgentOrchestrator.cs`, `RunManager.cs`, `AgentWorkflows.cs` | **Inconsistent / Unscalable** | Massive violation of single-responsibility principle. `AgentOrchestrator.cs` (1,870 LOC) contains prompting, model resolution, network HTTP dispatch, streaming JSON tokenization, Anthropic ephemeral cache header injection, error retries, reasoning effort conditionals, and diagnostic parsing. |
| **Model Provider Layer** | `ModelService.cs`, `ModelGateway.cs`, `AgentOrchestrator.cs` | **Broken Abstraction** | No unified provider abstraction exists. `ModelGateway.cs` is a dead 30-line facade. `ModelService.cs` streams completions for titles/chat without tools, while `AgentOrchestrator.cs` implements an entirely independent HTTP streaming client for tool calling. |
| **Tool Execution** | `AgentTools.cs`, `ToolExecutor.cs`, `NativeProcessRunner.cs` | **Fragile / Vibe-Coded** | 51 tools crammed into a single 2,890-line class with a monolithic switch statement. Tool parameter schemas are handwritten JSON objects duplicated between definitions and execution parsers. |
| **Persistence** | `SqliteDatabaseService.cs`, `PostgresDatabaseProvider.cs` | **Works Fine** | Clean ADO.NET / raw SQL schema creation and migration for conversations, messages, and terminal logs. |
| **Automation / Scheduling** | `SandboxScheduling.cs`, `TaskAndMemoryService.cs` | **Broken / Fake** | The system claims reminder and task automation, but `CreateReminderAsync` ([TaskAndMemoryService.cs:307](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/TaskAndMemoryService.cs#L307)) merely appends records to a JSON list. There is no timer, no background worker thread, and no cron dispatcher. |

---

### 3. Model-Agnosticism: Real Abstraction or Leaky Facade?

**Verdict: Leaky Facade with Critical Provider Breakages.**

The system is hardwired around the **OpenAI Chat Completions API schema** with opportunistic monkey-patching.

#### Evidence of Provider Leakage:
1. **Direct Anthropic Protocol Failure [CRITICAL BUG]:**  
   In [AgentOrchestrator.cs:1700](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L1700):
   ```csharp
   "anthropic" => $"{prov?.BaseUrl ?? "https://api.anthropic.com/v1"}/chat/completions"
   ```
   Anthropic does not have a `/chat/completions` endpoint. Anthropic’s API is `/v1/messages`. Anthropic does not accept OpenAI's `messages` array structure (it separates `system` prompt into a top-level field) nor OpenAI's `tools` schema (`input_schema` vs `parameters`). Swapping in an official Anthropic API key causes an immediate HTTP 404 failure.
2. **Ad-Hoc Anthropic Ephemeral Caching Injection:**  
   In [AgentOrchestrator.cs:317-333](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L317-L333) and [lines 373-380](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L373-L380):
   The code checks string equality on `providerId == "anthropic" || providerId == "openrouter"` and manually mutates the JSON DOM to attach `"cache_control": { "type": "ephemeral" }` to system blocks and tool definitions.
3. **Hardcoded OpenAI Reasoning Effort Conditions:**  
   In [AgentOrchestrator.cs:361-368](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L361-L368):
   ```csharp
   if (actualModel.StartsWith("o1") || actualModel.StartsWith("o3") || actualModel.Contains("gpt-5"))
   {
       requestPayload["reasoning_effort"] = ReasoningEffort.Normalize(_modelService.CurrentReasoningEffort);
   }
   ```
   Reasoning effort parameter handling is hardcoded using string prefix matching against specific OpenAI model names.

#### What breaks if I swap in a local or non-OpenAI model?
- **Ollama / Local GGUF:** If the model does not strictly support OpenAI function-calling grammar (`tools` with `tool_choice="auto"`), the model either emits raw XML/JSON into the content stream (which Gram fails to parse as a tool call) or returns an HTTP 400 Bad Request.
- **DeepSeek V3 / R1:** DeepSeek reasoning tokens (`<think>...</think>`) are processed by regex heuristics rather than standard schema properties ([AgentOrchestrator.cs:646-670](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L646-L670)).

---

### 4. Agent Loop Design & Failure Recovery

The loop in `AgentOrchestrator.RunAgentLoopAsync` ([AgentOrchestrator.cs:274-1045](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L274-L1045)) functions as follows:

```
[Start Run]
   │
   ├─► Resolve Model & Endpoint (BYOK vs System Key)
   │
   ├─► While (iteration < maxIterations && !cancelled):
   │     ├─► Trim & Compact Historical Tool Outputs (>3 steps old compacted to 250 chars)
   │     ├─► Dispatch HTTP POST stream to Model
   │     ├─► Stream chunks: ContentDelta, ThinkingDelta, ToolCall parsing
   │     ├─► If Tool Call requested:
   │     │     ├─► Evaluate Security & Request Approval if Risk > Safe
   │     │     ├─► Execute Tool (Direct ProcessRunner OR LocalAgent OR Strands)
   │     │     ├─► Check Self-Healing / Compiler Diagnostics
   │     │     └─► Append Tool Result to loopMessages as role="tool"
   │     │
   │     ├─► If Model finishes without Tool Call ──► Yield IsComplete = true ──► Break
   │     └─► If Network / HTTP Error ──► Yield Error ──► Break
   │
[End Run]
```

#### Evaluation of Failure Modes & Runaway Protection:
- **Loop / Runaway Protection:** **Fragile.**  
  Controlled strictly by `maxIterations` (default 50 in `AgentOrchestrator.cs:76`, though clamped to 8 or 4 in certain workflows in `AgentWorkflows.cs:123`). If an agent enters a circular loop (e.g., repeatedly editing a file, failing a test, and repeating the same faulty patch), it will exhaust all 50 iterations, consuming substantial token budget.
- **Context Window Management:** **Brittle Compaction.**  
  In [AgentOrchestrator.cs:308-345](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L308-L345), tool outputs older than the last 3 results are truncated to 250 characters (`"[Historical tool output compacted...]"`). There is no dynamic token-budget counting for user history. If a prompt plus file history exceeds the context window, the provider throws a context-length exceeded error and the run aborts completely.
- **Cancellation & Step Recovery:** **Works.**  
  CancellationToken linked through `AgentRunTracker.GetCancellationToken(runId)` ([AgentRunTracking.cs:42](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentRunTracking.cs#L42)) successfully aborts inflight HTTP requests and terminates child process trees via `proc.Kill(entireProcessTree: true)` ([NativeProcessRunner.cs:135](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/NativeProcessRunner.cs#L135)).

---

### 5. Tool & Sandbox Layer: Security Evaluation

#### A. Command Execution Sandbox: **High Risk**
- **The Reality:** Despite having a class named `AgentSecurityService.cs`, shell commands executed via `run_command` ([AgentTools.cs:1561](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentTools.cs#L1561)) execute **directly on the host OS via `zsh` or `bash`** under the user account running `Gram.Server`.
- **The "Sandbox":** The only defense is regex command blacklisting in `AgentSecurityService.cs` (`rm -rf /`, `mkfs`, fork bombs) and approval prompts.
- **Vulnerability:** Blacklist-based command security is notoriously easy to bypass (e.g., `base64 -d | sh`, python scripts, obscure shell expansions, or indirect scripts).

#### B. Path Traversal & File Access: **Works but Inconsistent**
- `SandboxPathGuard.TryResolve` ([SandboxPathGuard.cs](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/SandboxPathGuard.cs)) correctly checks `Path.GetFullPath` against the canonical workspace directory and rejects symbolic links targeting outside roots.
- However, file tools like `write_file` permit creating parent directories outside the workspace if relative path calculation fails on case-insensitive filesystems (macOS APFS).

#### C. Secret Exposure & "Vibe-Encryption": **Critical Risk**
- In [ModelService.cs:526-565](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/ModelService.cs#L526-L565):
  ```csharp
  var key = SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + Environment.MachineName + "GramSecretKeySalt2026"));
  ```
  The user's BYOK API keys (OpenAI, Anthropic, Gemini, Groq) are saved to `~/.gram/secure_keys.dat` using AES-256 with a key derived from the user's username, hostname, and a static string.
  **Any unprivileged local script or malware running on the user's machine can immediately decrypt all saved API keys.**
  Furthermore, `NativeProcessRunner.cs` cleans the child process environment using `EnvironmentScrubber.Scrub(psi)` ([NativeProcessRunner.cs:89](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/NativeProcessRunner.cs#L89)), but only strips known environment variable names. If a secret is saved in an unlisted variable or in a local file (`.env`), any shell tool execution has full read access to it.

---

### 6. State & Persistence Resilience

#### What happens on Crash or Restart?
- **In-Flight Run Loss:** If `Gram.Server` is killed or restarts during an active run, `AgentRunTracker`'s in-memory dictionary is wiped. The run state is permanently lost.
- **Client Desynchronization:** The client web UI / CLI polling `/api/v1/runs/{id}/events` receives a 404 Not Found error and hangs or crashes with an unhandled exception.
- **Dangling Filesystem Changes:** If a run was modifying files directly without git worktree isolation, partial or corrupted edits remain on disk with no undo log.

---

### 7. Observability & Debuggability

- **Token & Rate Limit Tracking: [FINE]**  
  `QuotaTracking.cs` ([QuotaTracking.cs:29-75](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/QuotaTracking.cs#L29-L75)) accurately observes `x-ratelimit-*` headers, latency, and token consumption per provider/model.
- **Agent Tracing & Run Replay: [FRAGILE / INSUFFICIENT]**  
  Runs are not stored in standard OpenTelemetry or trace spans. There is no structured JSONL transcript of agent reasoning steps persisted to disk on a per-run basis. The only record of tool calls is appended to the message object upon completion ([RunManager.cs:174](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/RunManager.cs#L174)). If a run fails halfway through, its execution trace vanishes.

---

### 8. Extensibility: What It Takes to Add Features

#### To Add a New Model Provider:
You must touch at least **6 files** across multiple subsystems:
1. `Gram.Blazor/Shared/ModelIds.cs`: Add model constant.
2. `Gram.Blazor/Shared/ModelService.cs`: Add to `DefaultModels` and `DefaultProviders`.
3. `Gram.Blazor/Shared/AgentOrchestrator.cs`: Add endpoint resolution in `ResolveTargetModel()` ([line 1695](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L1695)), handle auth header format, handle rate limits.
4. `Gram.Blazor/Shared/RateLimitCoordinator.cs`: Add provider rate-limiting parameters.
5. `Gram.Blazor/Pages/Settings.razor`: Add UI settings inputs for API keys.
6. `Gram.Blazor/Components/ModelIcon.razor`: Add vendor icon/badge SVG.

#### To Add a New Tool:
You must touch at least **4 files**:
1. `Gram.Blazor/Shared/AgentTools.cs`: Add JSON Schema in `GetAvailableTools()` AND append a `case "my_tool":` branch inside the 2,890-line `ExecuteToolAsync` method.
2. `Gram.Blazor/Shared/ToolNameHelper.cs`: Add display name mapping.
3. `Gram.Blazor/Shared/AgentSecurityService.cs`: Add permission / safety classification rules.
4. `Gram.Blazor/Components/ToolCallCard.razor`: Add UI rendering block.

---

### 9. Top Architectural Risks (Ranked)

1. **[CRITICAL] Broken Direct Model Agnosticism (Anthropic & Non-OpenAI Providers):**  
   `AgentOrchestrator.cs` makes direct HTTP calls to `https://api.anthropic.com/v1/chat/completions` with OpenAI schemas, breaking native Claude support out-of-the-box.
2. **[CRITICAL] Vulnerable Secret Encryption Key Derivation:**  
   `secure_keys.dat` uses predictable machine properties (`UserName + MachineName + Salt`) as an encryption key, providing false security.
3. **[HIGH] Lack of True Process Sandboxing:**  
   `run_command` executes arbitrary bash commands directly on the host machine without containerization, namespaces, or microVM isolation.
4. **[HIGH] Ephemeral Run State in RAM:**  
   `AgentRunTracker` stores all active runs and event buffers in memory dictionaries. Any restart or server crash drops runs and corrupts client state.
5. **[MEDIUM] Monolithic 2,890-Line `AgentTools.cs` God Class:**  
   Mixing presentations, databases, git commands, shell scripts, and reminders into a single switch statement prevents modular testing, extension, or verification.

---

### 10. Proposed Target Architecture

To transition Gram from a prototype to a robust, model-agnostic Codex alternative, the following architecture is required:

```
┌────────────────────────────────────────────────────────┐
│                   Clients Layer                        │
│    Gram Web (Blazor)   │   Gram CLI   │   Gram Desktop │
└───────────────────────────┬────────────────────────────┘
                            │ gRPC / SSE / REST
┌───────────────────────────▼────────────────────────────┐
│                    Gram Host Core                      │
│                                                        │
│  ┌──────────────────────────────────────────────────┐  │
│  │              Agent Execution Engine              │  │
│  │  - Pluggable Agent Workflows (Plan/Act/Verify)    │  │
│  │  - Durable Run State (Persisted SQLite Events)   │  │
│  │  - Context Manager (Tree-sitter AST + Compactor) │  │
│  └────────────────────────┬─────────────────────────┘  │
│                           │                            │
│  ┌────────────────────────┴─────────────────────────┐  │
│  │              Model Abstraction Layer             │  │
│  │  ┌───────────────┐ ┌──────────────┐ ┌──────────┐ │  │
│  │  │ OpenAI Driver │ │ Claude Driver│ │Gemini Drv│ │  │
│  │  └───────────────┘ └──────────────┘ └──────────┘ │  │
│  │    * Standardized Request/Response & Tool Calls  │  │
│  └────────────────────────┬─────────────────────────┘  │
│                           │                            │
│  ┌────────────────────────┴─────────────────────────┐  │
│  │                Tool & Sandbox Layer              │  │
│  │  ┌───────────────────────┐ ┌───────────────────┐ │  │
│  │  │  Isolated Workspace   │ │ Pluggable Tool    │ │  │
│  │  │  Sandbox (Docker/App) │ │ Registry (LSP/Git)│ │  │
│  │  └───────────────────────┘ └───────────────────┘ │  │
│  └──────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────┘
```

---

## Phase 2: Prompt Structuring & Context Engineering

### 1. Comprehensive Prompt Inventory

| Prompt Identifier | File Location | Purpose & Trigger | Size (Chars / Est. Tokens) | Model Assumptions & Bias | Quality & Status |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **`DefaultBasePrompt`** | [`GramSystemPrompt.cs:22-96`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/GramSystemPrompt.cs#L22-L96) | Default system prompt injected into every agent run and conversation turn. | 6,850 chars (~1,700 tokens) | Assumes high-end frontier models (Claude 3.5 Sonnet, GPT-4o). Heavily bloated with disparate domains. | **[CRITICAL / BROKEN]** Polluted with real estate instructions, slide design rules, and contradictory scaffolding blocks. |
| **`VoiceConversationPrompt`** | [`GramSystemPrompt.cs:7-20`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/GramSystemPrompt.cs#L7-L20) | Active during real-time WebRTC/AssemblyAI voice conversation mode. | 950 chars (~240 tokens) | Assumes fast speech models; explicitly forbids Markdown/code blocks. | **[FINE]** Focused, concise, and well-constrained for conversational TTS. |
| **`PureChatModePrompt`** | [`AgentOrchestrator.cs:152`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L152) | Injected when tools are physically disabled or model is in `ChatOnly` tier. | 65 chars (~16 tokens) | Generic. Inlined magic string. | **[WORKS BUT FRAGILE]** Bare minimum; lacks guidance on markdown or formatting. |
| **`ArchitecturalPlannerSubagent`** | [`SubagentEngineService.cs:55-60`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/SubagentEngineService.cs#L55-L60) | System prompt for planning subagent (`role=planner`). | 320 chars (~80 tokens) | Assumes model intuitively knows how to format plans without a schema. | **[FRAGILE]** No JSON or Markdown schema defined. |
| **`ResearchSubagent`** | [`SubagentEngineService.cs:67-72`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/SubagentEngineService.cs#L67-L72) | System prompt for codebase exploration and web research subagent. | 290 chars (~72 tokens) | Assumes model knows when to stop researching. | **[FRAGILE]** No output format or synthesis criteria. |
| **`CoderSubagent`** | [`SubagentEngineService.cs:79-84`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/SubagentEngineService.cs#L79-L84) | System prompt for isolated sandbox coder subagent. | 230 chars (~58 tokens) | Assumes model understands worktree sandbox semantics. | **[CRITICAL / VAGUE]** 34 words. Zero error handling, no test-before-complete requirement. |
| **`TestingSubagent`** | [`SubagentEngineService.cs:91-96`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/SubagentEngineService.cs#L91-L96) | System prompt for verification subagent. | 210 chars (~52 tokens) | Assumes tool runner returns raw stderr. | **[FRAGILE]** No failure classification guidance. |
| **`CodeReviewSubagent`** | [`SubagentEngineService.cs:103-108`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/SubagentEngineService.cs#L103-L108) | System prompt for reviewing diffs. | 180 chars (~45 tokens) | Assumes model sees clean unified diffs. | **[FRAGILE]** Lacks review rubric or severity levels. |
| **`PresentationSubagent`** | [`SubagentEngineService.cs:114-119`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/SubagentEngineService.cs#L114-L119) | Content generator for HTML slide decks. | 260 chars (~65 tokens) | Assumes external schema is supplied in turn. | **[INCONSISTENT]** Feature bloat in a coding tool. |
| **`DeterministicSummaryPrompt`** | [`ContextBuilder.cs:309-319`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/ContextBuilder.cs#L309-L319) | Summarization prompt intended for context compaction. | 320 chars (~80 tokens) | None. | **[DEAD CODE]** Method is never called; context builder uses string slicing instead. |
| **`ModelProbePrompt`** | [`ModelCapabilityProbeService.cs:311`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/ModelCapabilityProbeService.cs#L311) | Used to evaluate if a model supports surgical edits. | 450 chars (~110 tokens) | Assumes strict JSON output format. | **[FINE]** Unit test probe. |
| **Tool Definitions (51 Tools)** | [`AgentTools.cs:119-940`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentTools.cs#L119-L940) | Descriptions and JSON parameter schemas presented to LLM for tool calling. | ~18,000 chars (~4,500 tokens) | Strictly formatted for OpenAI JSON Schema parser. | **[UNSCALABLE]** Monolithic inline instantiation. High token overhead per turn. |

---

### 2. Prompt Architecture & Engineering Critique

#### A. Lack of Centralization and Versioning
- Prompts are scattered haphazardly: some reside in static classes (`GramSystemPrompt.cs`), others are hardcoded in orchestrator methods (`AgentOrchestrator.cs:152`), others are dynamically constructed via `StringBuilder` in `ContextBuilder.cs`, and subagent prompts are string literals inside method bodies in `SubagentEngineService.cs`.
- There is **no template engine** (like Scriban, Handlebars, or Jinja2) and **no prompt versioning**. A change to `DefaultBasePrompt` affects all models globally without backwards-compatibility checks or AB regression scoring.

#### B. The "Vibe-Coding" Pollution in `DefaultBasePrompt`
A rigorous inspection of [`GramSystemPrompt.cs`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/GramSystemPrompt.cs#L22-L96) reveals egregious examples of copy-pasted, domain-polluted instructions:

1. **The Real Estate / Apartment Hunting Hallucination:**  
   In [`GramSystemPrompt.cs:38-45`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/GramSystemPrompt.cs#L38-L45):
   ```markdown
   When asked to research, find, compare, or investigate real-world entities (such as apartments, 
   rentals, pricing, products, hardware specs, documentation, local services, hotels, or venues)...
   Always extract and present specific entity names, exact pricing/rates, street addresses or neighborhoods, 
   unit configurations (bedrooms, bathrooms, square footage)...
   When the user asks you to find or search for options (e.g. "what are some apartments under 1000", 
   "search those sites"), NEVER give vague generic advice... (such as Apartments.com, Zillow, Rentable, Zumper)...
   ```
   **Staff Critique:** This codebase is intended to be a **model-agnostic alternative to OpenAI Codex**. Injecting 250 words of hardcoded apartment hunting rules (Zillow, Rentable, bedroom counts, lease conditions) into the global system prompt of every software engineering run wastes valuable context tokens, confuses reasoning models, and degrades the agent's focus on software engineering tasks.

2. **The Slide Deck & Presentation Clutter:**  
   In [`GramSystemPrompt.cs:86-90`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/GramSystemPrompt.cs#L86-L90):
   ```markdown
   ### 5. Interactive Presentations & Slide Decks Protocol
   - Presentation Quality: When generating slide decks or presentations, create clean, high-impact 16:9 layouts.
   ```
   This is another hackathon feature wedged directly into the core engineering system prompt.

3. **Structural Syntax & Numbering Bugs:**  
   In `GramSystemPrompt.cs`, Section 5 appears **twice consecutively**:
   - Line 78: `### 5. Subagent Engine & Multi-Agent Delegation Protocol`
   - Line 86: `### 5. Interactive Presentations & Slide Decks Protocol`
   Models that parse structural headers for constraint hierarchies receive contradictory numbering.

4. **Self-Sabotaging Scaffolding Rule [CRITICAL BUG]:**  
   In [`GramSystemPrompt.cs:65-66`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/GramSystemPrompt.cs#L65-L66):
   ```markdown
   When the user asks to start a new project, scaffold an application from scratch...
   Phase 1 (Ideation & Requirements): First explore requirements...
   Do NOT call write_file, edit_file, or run_command in this phase; deliberate with the user until the blueprint is clear.
   ```
   **Staff Critique:** If a user types *"Create a new console app in C# with a Hello World file"*, the prompt explicitly forbids the agent from writing files or executing terminal commands, forcing it to stall and ask clarification questions even when the user gave an explicit command.

5. **Prompts Directing Models to Dead / Inactive Tools:**  
   Line 94 instructs: `"When the user asks to be reminded... invoke set_reminder"`. As proven in Phase 1, `set_reminder` has no timer or notification worker; the prompt instructs the model to use an illusionary feature.

---

### 3. Tool Definitions & Schema Precision

Tool definitions in `AgentTools.cs` ([lines 119-940](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentTools.cs#L119-L940)) suffer from severe cross-model transfer problems:

1. **Massive Context Consumption:**  
   All 51 tools are serialized into the JSON payload on every single iteration when tools are enabled. This consumes **~4,500 prompt tokens per turn** before the user's prompt or files are even added.
2. **Ambiguous Line Indexing in File Tools:**  
   In `view_file` ([`AgentTools.cs:129-130`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentTools.cs#L129-L130)), `startLine` and `endLine` are described as `"Optional 1-indexed start line"`. However, in `edit_file` ([lines 166-167](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentTools.cs#L166-L167)), they are called `"anchor lines to restrict replacement scope"`. Smaller or open-weights models (such as Llama 3 8B or Mistral 7B) frequently confuse anchor bounds with replacement lines and delete large sections of adjacent code.
3. **No Dynamic Tool Filtering Based on Intent:**  
   Whether the user asks to "fix a typo" or "write a complex multi-file service", Gram sends database DML tools, slide deck tools, git PR tools, and subagent spawners simultaneously.

---

### 4. Context Engineering & Window Management

```
┌────────────────────────────────────────────────────────────────────────┐
│ Context Window Layout (Assembled by ContextBuilder.cs)                 │
├────────────────────────────────────────────────────────────────────────┤
│ 1. Base System Prompt (~1,700 tokens) [Polluted with Zillow/Slides]   │
│ 2. User Custom Instructions (SettingsService)                          │
│ 3. Memory & Rules Context (TaskAndMemoryService)                       │
│ 4. Scoped Project Knowledge Base (Files up to 30,000 chars each)       │
│ 5. Rolling Context Summary (Deterministic text slice, NOT LLM summary)│
│ 6. Workspace Attachments (Parsed Text / Document dumps)                │
│ 7. Workspace Artifacts List                                            │
│ 8. Repository AST Map (RepositoryMapService / PageRank symbol graph)   │
│ 9. Recent Message History: TakeLast(16) [NO TOKEN COUNTING]           │
│ 10. Tools Catalog (51 tools in JSON schema: ~4,500 tokens)             │
└────────────────────────────────────────────────────────────────────────┘
```

#### The Failure Modes:
- **Blind Message Slicing (`TakeLast(16)`):**  
  In [`ContextBuilder.cs:234-236`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/ContextBuilder.cs#L234-L236):
  ```csharp
  var recentMessages = historyMessages.Count > maxRecentMessages
      ? historyMessages.TakeLast(maxRecentMessages).ToList()
      : historyMessages.ToList();
  ```
  The windowing logic is purely count-based. If a user pastes a large stack trace or test log, or a tool returns a 40,000-character output, 16 messages easily exceed the 8k–32k context windows of standard open-weights or local Ollama models.
- **What Happens at the Limit? [CRITICAL CRASH]**  
  There is **zero budget-aware truncation or fallback**. When the assembled payload exceeds the model's context length, the cloud provider or Ollama returns an HTTP 400 (`context_length_exceeded` or `maximum context length exceeded`). Gram handles this by simply setting `ErrorMessage = ex.Message` and aborting the run ([`AgentOrchestrator.cs:450`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L450)). The agent does not self-heal or compact and retry.

---

### 5. Prompt Evaluation & Regression Harness

**Current State:** There is **zero prompt evaluation** in the codebase. When someone tweaks `GramSystemPrompt.cs`, there is no automated test measuring whether coding performance, tool-call accuracy, or instruction adherence improved or broke.

#### Proposed Minimal Prompt Regression Harness (`GramPromptEvalHarness`)

A minimal, deterministic C# evaluation harness should be established in `Gram.Blazor.Tests`:

```csharp
public sealed record PromptEvalTestCase(
    string TestId,
    string UserPrompt,
    string[] ExpectedToolsCalled,
    string[] ForbiddenTools,
    string[] MustContainSubstrings,
    bool MustNotAskQuestions);

public class PromptRegressionTests
{
    [Theory]
    [InlineData("Create a new dotnet console app in src/cli", new[] { "run_command" }, new[] { "search_web" })]
    [InlineData("Fix the null reference in UserService.cs line 42", new[] { "view_file", "edit_file" }, new[] { "write_file" })]
    public async Task BasePrompt_DirectAction_TriggersCorrectTools(string prompt, string[] expectedTools, string[] forbidden)
    {
        // 1. Assemble prompt with ContextBuilder
        // 2. Mock model response or run against fast eval model (e.g. Gemini Flash Lite)
        // 3. Assert tool call selections and zero conversational stall
    }
}
```

---

### 6. Standard-Setting Prompt Rewrites

Below are professional-grade rewrites of the two weakest prompts in the system, engineered to strict OpenAI Codex / Anthropic Claude Code standards.

#### Rewrite 1: Core Autonomous Engineering System Prompt
*(Replacing the bloated, Zillow-polluted `DefaultBasePrompt` in [`GramSystemPrompt.cs`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/GramSystemPrompt.cs#L22-L96))*

```markdown
<identity>
You are Gram, an elite autonomous software engineer and pair programmer.
Your purpose is to inspect codebases, design robust solutions, write precise code, and verify correctness using your available tools.
</identity>

<operating_principles>
1. Action-Oriented & Direct:
   - When the user asks you to create, modify, test, or inspect code, proceed directly with tool actions.
   - Do not stall with conversational filler, pre-action narration, or requests for permission unless a destructive action outside the workspace is strictly required.

2. Surgical Precision:
   - Always view target files with `view_file` before making modifications.
   - Prefer surgical replacements via `edit_file` over whole-file overwrites (`write_file`). Never emit placeholder comments like "// rest of implementation".
   - Maintain existing project conventions, indentation, and formatting.

3. Mandatory Verification Loop:
   - After applying code changes, execute builds or test suites (`run_command` or `run_tests`) to confirm your changes compile and pass.
   - If a test or build fails, inspect the diagnostic output, analyze the root cause, and apply a surgical fix. Never declare completion with failing tests.

4. Workspace Safety:
   - Confine all file modifications strictly to the designated workspace root.
   - Never output or expose credentials, secrets, or unscrubbed tokens in your messages.

5. Output Discipline:
   - When complete, provide a concise summary (maximum 2 short paragraphs):
     - Paragraph 1: Exact changes made, files modified, and test verification results.
     - Paragraph 2: Architectural notes, trade-offs, or next steps.
</operating_principles>
```

#### Rewrite 2: Coder Subagent Prompt
*(Replacing the vague 34-word prompt in [`SubagentEngineService.cs:79-84`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/SubagentEngineService.cs#L79-L84))*

```markdown
<role>
You are a specialized Coder Subagent operating inside an isolated workspace sandbox.
Your sole mission is to implement assigned code changes completely, accurately, and cleanly.
</role>

<task_execution_contract>
1. Input: You will be provided with a specific task instruction, target files, and relevant context.
2. Step 1 (Inspection): Use `view_file` to inspect the exact line numbers and context of all files to be modified.
3. Step 2 (Modification): Use `edit_file` for surgical changes or `write_file` for new files. Do not delete existing unrelated methods or comments.
4. Step 3 (Verification): If a build or test command is applicable, run it using your command runner to verify zero syntax or compilation errors.
5. Exit Criteria:
   - Respond ONLY when code edits are finished.
   - Your final message MUST conform to this exact markdown format:
     ### Implementation Summary
     - **Files Changed:** [list of relative file paths]
     - **Changes Made:** [brief bullet points of exact additions/removals]
     - **Verification Status:** [PASS / FAIL / NOT APPLICABLE with details]
</task_execution_contract>
```

---

## Phase 3: Modeling & Model Layer

### 1. Model Selection, Configuration & Routing

The model catalog is managed by `ModelService.cs` ([`ModelService.cs:21-35`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/ModelService.cs#L21-L35)) and hardcoded defaults in `ModelIds.cs`.

```
                  ┌─────────────────────────────────────────┐
                  │ Model Selection in UI (Workspace.razor) │
                  └────────────────────┬────────────────────┘
                                       │ Selected ModelId
                                       ▼
                  ┌─────────────────────────────────────────┐
                  │   AgentOrchestrator.ResolveTargetModel  │
                  └────────────────────┬────────────────────┘
                                       │
         ┌─────────────────────────────┴────────────────────────────┐
         ▼                                                          ▼
[Platform Alias (gram-q*)]                                 [Direct Provider BYOK]
- gram-q: Gemini / Groq / Vercel                           - Look up ProviderConfig in providers.json
- gram-q+: Vercel Gateway                                  - Retrieve API Key from secure_keys.dat
- gram-q++: Google Gemini Flash                            - Route to endpoint:
                                                             • groq -> api.groq.com/openai/v1
                                                             • google -> generativelanguage.googleapis.com
                                                             • anthropic -> api.anthropic.com/v1 (BROKEN)
                                                             • default -> openrouter.ai/api/v1
```

#### Capability Registry Evaluation:
In `ModelService.cs`, the model metadata entity (`GramModel`) defines:
- `Id`, `Name`, `Provider`, `Description`, `ContextLength`, `IsFavorite`, `IsEnabled`, `CodingTier`.

#### Critical Registry Deficiencies:
1. **Missing Cost Metrics:** There is **no pricing metadata** (input cost, output cost, cached token discount per million tokens). Gram cannot report the financial cost of a multi-turn run to the user.
2. **Missing Modality & Vision Flags:** There is no field declaring whether a model accepts image attachments (`image/png`, `image/jpeg`). If a user attaches an image and selects a text-only model (e.g. `llama-3.3-70b`), the request is sent with image blocks and fails with an upstream HTTP 400.
3. **Missing Tool Calling & Structured Output Metadata:** No capability flags indicate whether a model supports OpenAI function calling, JSON mode, or system instructions.

---

### 2. Handling of Differences Across Providers & Local Models

**Verdict: Fragile Monolithic Normalizer with Ad-Hoc Vendor Hacks.**

Rather than using polymorphic provider drivers (e.g. `IOpenAiDriver`, `IAnthropicDriver`, `IGeminiDriver`), `AgentOrchestrator.cs` forces all providers through a single loop with hardcoded string checks:

1. **Google Gemini Thought Signature Replay Hack:**  
   In [`AgentOrchestrator.cs:747-758`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L747-L758):
   ```csharp
   if (!string.IsNullOrWhiteSpace(kvp.Value.thoughtSig) && (providerId == "google" || endpoint.Contains("googleapis.com")))
   {
       // Gemini's OpenAI-compatible API requires the signature to be
       // replayed in this exact location on the assistant tool call.
       callObj["extra_content"] = new JsonObject
       {
           ["google"] = new JsonObject { ["thought_signature"] = kvp.Value.thoughtSig }
       };
   }
   ```
   A proprietary Google Thought Signature requirement is patched directly into the main tool-call serialization loop.
2. **DeepSeek R1 / V3 Reasoning Token Extraction:**  
   In [`AgentOrchestrator.cs:551-554`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L551-L554) and [`line 770`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L770):
   Reasoning deltas are extracted by inspecting multiple vendor-specific keys (`reasoning_content`, `reasoning`, `thought`), but replayed back to the API strictly under `reasoning_content` if `providerId == "deepseek"`.
3. **OpenAI Reasoning Effort Hardcoding:**  
   In [`AgentOrchestrator.cs:361-368`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L361-L368), string checks against `o1`, `o3`, and `gpt-5` inject the parameter `reasoning_effort`.
4. **Local Models (Ollama):**  
   Ollama is treated as a generic OpenAI endpoint (`http://localhost:11434/v1/chat/completions`). If an Ollama model emits malformed tool call JSON or mixes narration with tool arguments, Gram fails to parse it.

---

### 3. Fallbacks, Retries, Rate Limits & Error Recovery

#### A. Automatic Fallbacks: **Fragmented & Hardcoded**
- The only fallback in the entire codebase is found in [`AgentOrchestrator.cs:459-472`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L459-L472):
  If the platform model `gram-q` hits an HTTP 429 on `llama-3.3-70b-versatile`, it dynamically downgrades to `llama-3.1-8b-instant` on Groq.
- **No other model or provider has fallback support.** If GPT-4o, Claude 3.5 Sonnet, or Gemini 1.5 Pro throws a 429, 500, or 503, the run immediately crashes.

#### B. Transient Retries & Network Disconnects: **Zero Resilience**
- If an HTTP request encounters a momentary network blip, TLS reset, or socket timeout, `AgentOrchestrator` catches the exception ([`AgentOrchestrator.cs:440`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L440)), sets `ErrorMessage = "Network request failed: ..."`, and **aborts the run completely**.
- There is **no exponential backoff**, no jitter, and no retry policy (e.g. Polly). In an agentic loop running 20+ turns, a single transient failure throws away all preceding work.

#### C. Rate Limit & Quota Coordination: **Fine**
- `RateLimitCoordinator.cs` and `QuotaTracking.cs` accurately inspect `Retry-After` headers and maintain cooldown timestamps per `QuotaRequestKey`.

---

### 4. Output Validation & Handling Malformed Responses

```
[SSE Delta: tool_calls] ──► String Concatenation in StringBuilder ──► Tool Execution
```

#### Critical Vulnerabilities in Output Parsing:
1. **Zero JSON Argument Sanitization:**  
   In [`AgentTools.cs:960`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentTools.cs#L960):
   ```csharp
   var args = JsonNode.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
   ```
   If the model emits markdown fences (e.g. ```` ```json {"filePath": "..."} ``` ````), trailing commas, unescaped quotes, or truncates JSON due to token limits, `JsonNode.Parse` throws an unhandled `JsonException`.
   The tool catches this in a generic handler and returns `result.IsError = true; result.Output = ex.Message;`.
   **There is no JSON repair heuristic (e.g. balance braces, strip backticks, auto-escape unescaped newlines).**
2. **Missing Tool Name Validation:**  
   If a model hallucinates a non-existent tool name (e.g. `create_file` instead of `write_file`, or `execute_bash` instead of `run_command`), the switch statement in `AgentTools.cs` falls through to line 2855:
   `result.Output = $"Unknown or unsupported tool '{name}'."; result.IsError = true;`
   While this reports an error to the model, the model often repeats the hallucinated tool in the next iteration until max iterations are reached.

---

### 5. Model Evaluation & Benchmark Analysis

#### The "Orphaned Probe" Discovery:
The repository contains a 452-line class: [`ModelCapabilityProbeService.cs`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/ModelCapabilityProbeService.cs#L18-L49), designed to test:
1. `ChatReasoning`: Logical inference verification.
2. `ToolCallingGrammar`: Verifies JSON function calling syntax adherence.
3. `SurgicalPatchAccuracy`: Verifies whitespace and line-level patch precision.
4. `ContextCapacity`: Verifies negative constraints across large prompts.

**Audit Finding:** `ModelCapabilityProbeService.RunBatteryAsync` is **never called anywhere in the active application**. It is completely disconnected from the UI, settings, and agent loop. Every model in `ModelService.cs` is hardcoded to `ModelCodingSupportTier.VerifiedAutonomous` regardless of capability.

#### Proposed Gram Model Benchmark Suite (`GramBench`)

To objectively measure whether a model is qualified for autonomous coding in Gram, the following 5-task benchmark must be implemented:

| Test ID | Task Name | Input Prompt | Success Verification Criteria |
| :--- | :--- | :--- | :--- |
| **GB-01** | Tool Selection Precision | "Inspect line 40 to 60 of src/App.cs" | Must invoke `view_file` with `filePath="src/App.cs"`, `startLine=40`, `endLine=60`. Zero conversational stall. |
| **GB-02** | Whitespace-Preserving Patch | "Change variable `maxRetries = 3` to `5` in Config.cs" | Must invoke `edit_file` with exact indentation and context. Target must match existing file content. |
| **GB-03** | Compile-Error Recovery Loop | Introduces broken syntax in a stub class and triggers build | Model must view error diagnostics, call `edit_file` to fix syntax, and re-run build until exit code is 0. |
| **GB-04** | Negative Constraint Adherence | "Explain the architecture of UserService.cs. Do not modify any files." | Must invoke `view_file` or respond directly. Strictly forbidden from calling `write_file`, `edit_file`, or `run_command`. |
| **GB-05** | Multi-File Atomic Changeset | "Rename interface `IAuth` to `IAuthenticator` across 3 files" | Must invoke `apply_changeset` with coherent updates across all 3 files without breaking references. |

---

### 6. Cost & Latency Awareness

- **Latency Tracking: [FINE]**  
  `Stopwatch` instances wrap model requests and tool executions, recording millisecond timing in `AgentStepUpdate.LatencyMs` ([`AgentStepUpdate.cs:23`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentOrchestrator.cs#L23)).
- **Cost Awareness: [COMPLETELY ABSENT]**  
  Gram does not calculate or display financial costs anywhere in the application. Neither the UI nor the database stores pricing rates for OpenAI, Anthropic, or Groq models. Users running long agent loops have no visibility into accrued expenses.

---

## Phase 4: Code Generation & Editing Quality

### 1. Code Change Production Mechanisms

Gram employs three distinct mechanisms for generating and modifying code on the local filesystem:

| Mechanism / Tool | File Implementation | Operating Principle | Reliability & Failure Modes | Health Rating |
| :--- | :--- | :--- | :--- | :--- |
| **`write_file`** | [`AgentTools.cs:1016-1074`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentTools.cs#L1016-L1074) | Whole-file replacement via `File.WriteAllTextAsync`. | **High Risk.** Prone to dropping unrelated methods, deleting imports, or truncating large source files (>300 lines) due to model completion token limits or hallucinated "// rest of code here" comments. | **Works but Fragile** |
| **`edit_file`** | [`SurgicalPatchEngine.cs:13-153`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/SurgicalPatchEngine.cs#L13-L153) | Multi-tiered surgical chunk replacement with line anchors. | **High Reliability.** 4-tier fallback: Tier 1 (exact match), Tier 2 (whitespace/CRLF normalization), Tier 3 (boundary anchor matching), Tier 4 (diagnostic failure reports). | **[FINE] Outstanding Implementation** |
| **`apply_changeset`** | [`AgentTools.cs:1149-1302`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentTools.cs#L1149-L1302) | Multi-file transactional atomic batch modification. | Pre-validates all edits in a dry-run before writing. Automatically captures a workspace checkpoint and rolls back all files if any single file patch fails. | **[FINE] Robust** |

#### In-Depth Review of `SurgicalPatchEngine`:
The `SurgicalPatchEngine` is one of the highest-quality engineering artifacts in the repository. Unlike standard regex search-and-replace tools that fail when an LLM hallucinated indentation or normalized CRLF to LF, `SurgicalPatchEngine`:
1. Measures the exact leading whitespace of the existing code and adjusts the replacement lines to match the target's indentation ([`SurgicalPatchEngine.cs:193-215`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/SurgicalPatchEngine.cs#L193-L215)).
2. Provides line-bounded replacement when `startLine` and `endLine` are supplied, preventing accidental replacements of repeated tokens in large files ([`lines 36-75`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/SurgicalPatchEngine.cs#L36-L75)).
3. Emits actionable failure reports (`BuildFailureDiagnosis`) showing exactly where the target string diverged from the actual file lines.

---

### 2. Repository Understanding & Code Exploration

#### How the Agent Finds Relevant Files:
1. **File Scrape & PageRank Graph:** [`RepositoryMapService.cs:28-80`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/RepositoryMapService.cs#L28-L80) constructs a `CodeDependencyGraph` and calculates PageRank scores to identify architectural hub files.
2. **Brittle Regex Symbol Scanning [CRITICAL WEAKNESS]:**  
   In [`RepositoryMapService.cs:30-48`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/RepositoryMapService.cs#L30-L48), symbol discovery in the main C# runtime relies on hardcoded regular expressions:
   ```csharp
   private static readonly Regex SymbolPattern = new(@"\b(class|record|interface|struct|enum|trait|type)\s+([A-Za-z_][A-Za-z0-9_]*)...");
   private static readonly Regex ImportPattern = new(@"\busing\s+(?:static\s+)?([A-Za-z0-9_.]+);|\b(?:import|from)...");
   ```
   **Staff Critique:** Regex-based AST extraction is brittle. It fails on multi-line method signatures, C# primary constructors, generic constraints, C++ templates, and Python decorators.
3. **The Quarantined Tree-sitter AST Engine:**  
   The repository contains a robust, real Tree-sitter AST symbol extractor in [`engine/ast_symbol_extractor.py`](file:///Users/taufeeqali/projects/gram/engine/ast_symbol_extractor.py). However, because `launch.sh` does not launch `engine/server.py`, the C# client (`GramEngineClient.cs`) detects that the sidecar is offline and permanently falls back to the inferior regex engine.
4. **Handling Large Repositories (>1,000 Files):**  
   `RepositoryMapService.BuildDependencyGraphAsync` scans files synchronously across the disk on the server host. In repositories with large `node_modules`, `obj/`, or deep directories, this causes noticeable latency and high memory spikes during context assembly.

---

### 3. Verification Loop & Self-Correction

**Verdict: Solid Verification Infrastructure with Pre-Completion Quality Gate.**

Gram features an unusually thorough automated verification and repair loop:

```
[Agent Modifies Files] ──► Record in modifiedFilesHistory
                                 │
                                 ▼
                     [Model Finishes Responses]
                                 │
                                 ▼
          ┌──────────────────────────────────────────────┐
          │ Pre-Completion Quality Gate Check            │
          │ (AgentOrchestrator.cs:625-695)               │
          └──────────────────────┬───────────────────────┘
                                 │
            ┌────────────────────┴────────────────────┐
            ▼                                         ▼
   [Verification Passed]                    [Diagnostics Failed]
   - Emit Success                           - Parse Errors via CompilerDiagnosticParser
   - Conclude Run                           - Compute Error Hash (Check Repetition)
                                            - Mask Tools: Force Repair-Only Tools
                                            - Inject Error Feedback Turn into Agent Loop
                                            - If Repeating: Roll Back to Checkpoint
```

#### Key Capabilities:
- **Ecosystem Auto-Detection:** [`ProjectValidationDetector.cs`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/ProjectValidationDetector.cs) detects .NET (`dotnet build`/`dotnet test`), Node (`npm test`), Rust (`cargo test`), Go (`go test`), and Python (`pytest`).
- **Compiler Diagnostic Parsing:** [`CompilerDiagnosticParser.cs`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/CompilerDiagnosticParser.cs) parses file, line, column, error code, and error messages, and computes an MD5 error hash to detect runaway circular repair attempts.
- **Dynamic Tool Masking:** When self-healing is triggered, non-repair tools (like presentations, web search, database DML) are masked out so the model is forced to focus strictly on fixing the compilation errors.

---

### 4. Safety of Edits: Diff Review, Rollback & Race Conditions

#### A. Snapshot Checkpoints & Rollback: **[FINE]**
Before executing any `write_file` or `edit_file`, [`WorkspaceCheckpointService.CapturePreWriteSnapshotAsync`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/WorkspaceCheckpointService.cs) copies the original file bytes into a local checkpoint cache (`~/.gram/checkpoints/`). If an edit is corrupted, the user or agent can invoke `rollback_checkpoint`.

#### B. Git Integration & Worktrees: **Inconsistent**
- The repository includes [`GramBoxWorktreeManager.cs`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/GramBoxWorktreeManager.cs), which can create isolated git worktrees (`gram/sb-...`) and apply changes back via git patches.
- **The Gap:** Worktrees are **only used when subagents or explicit sandboxes are launched**. Standard chat runs operate directly in the user's live working directory. File modifications made via shell commands (`run_command: sed ...`, `echo ... > file`) bypass checkpoints and diff viewers entirely.

#### C. Protection Against Clobbering User Edits: **[CRITICAL GAP]**
- When `edit_file` or `write_file` executes, it reads the file from disk, applies the edit, and overwrites the file.
- **There is no check against file modification timestamps (mtime) or git dirty state.**
- If a user is actively editing a file in their editor (VS Code, Rider, Xcode) while Gram is running an agent turn, Gram's file write will **silently overwrite the user's uncommitted manual changes** with no merge conflict warning.

---

### 5. Gap Analysis: Gram vs. OpenAI Codex & Claude Code

| Feature / Capability | OpenAI Codex / Claude Code / Cursor | Gram (Current State) | Severity / Gap Rating |
| :--- | :--- | :--- | :--- |
| **AST & Symbol Navigation** | In-process LSP (Language Server Protocol) or Tree-sitter AST with 100% type definition accuracy. | Brittle C# regular expressions. True Tree-sitter engine is quarantined in an unlaunched Python sidecar. | **[CRITICAL GAP]** |
| **Interactive Diff Presentation** | Interactive terminal diff / unified review before changes are committed to disk. | Directly mutates files on disk; diff viewer in Blazor is purely retrospective. | **[HIGH GAP]** |
| **Context Compaction** | LLM-driven structured summarization preserving technical decisions and active state. | Count-based `TakeLast(16)` slicing; deterministic text trimmer; unhandled 400 error on overflow. | **[CRITICAL GAP]** |
| **File Edit Protection** | Detects local file changes; prompts user if file was modified outside the agent session. | Silently overwrites files; no mtime or dirty checks. | **[HIGH GAP]** |
| **Compilation Verification** | Pre-commit compiler and type-checker gates. | **Implemented well via `Pre-Completion Quality Gate` and `CompilerDiagnosticParser`.** | **[COMPARABLE]** |

---

## Phase 5: UI / UX

### 1. Product vs. Demo Evaluation

Gram’s UI is presented via Blazor Server (`Gram.Blazor/Pages/Workspace.razor`) and styled with a monolithic 232 KB stylesheet. When evaluated against professional developer tools (Cursor, Claude Code, Windsurf), Gram’s UI reveals severe identity dissonance:

| UI Dimension | Evaluation & Evidence | Severity Rating |
| :--- | :--- | :--- |
| **Information Architecture** | **Three Apps Stapled Together:** Chat canvas (`DesktopCanvas.razor`), terminal runners, slide presentation viewer (`HtmlPresentationRenderer.cs`), and mascot avatar (`GramCharacter.razor`) live in the same interface without a unified workflow. | **Works but Inconsistent** |
| **Core Flow: Task Start** | **Friction:** Starting a coding task requires navigating model dropdowns, selecting workspace roots, and bypassing prompt rules that instruct the model not to write files immediately. | **Works but Fragile** |
| **Core Flow: Progress Watching** | **Hidden Thinking State:** `DesktopSession.cs:535` parses thinking tokens into `DraftThinking`, but [`DesktopMessageList.razor:186-190`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/clients/Gram.Desktop/Components/CanvasArea/DesktopMessageList.razor#L186-L190) only renders a static pulsing dot: `"Thinking & reasoning..."`. The user cannot inspect deep reasoning or tool arguments while in progress. | **Broken** |
| **Core Flow: Change Review** | **Retrospective Only:** [`WorkspaceChangeReviewBar.razor:8-55`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/clients/Gram.Desktop/Components/CanvasArea/WorkspaceChangeReviewBar.razor#L8-L55) displays files modified *after* they have already been written to disk. There is no pre-modification diff review or approval step. | **Works but Fragile** |
| **Core Flow: Cancellation** | **Cooperative Delay:** Clicking Cancel in [`ComposerDock.razor`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/clients/Gram.Desktop/Components/Composer/ComposerDock.razor) signals `CancellationTokenSource`, but in-flight shell child processes spawned by `AgentTools.cs:1561` continue executing in the background until OS teardown. | **Works but Fragile** |
| **Error States** | **Raw JSON Dumps:** When model provider limits or context overflows occur (e.g. Anthropic 400), [`DesktopSession.cs:541`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/clients/Gram.Desktop/DesktopSession.cs#L541) appends `DraftResponse += $"\n\n[Error: {err}]"`, dumping raw unformatted exception strings into the chat bubble. | **Broken** |

---

### 2. State Honesty: What the UI Shows vs. What Happens

1. **Hidden Thinking & Reasoning Tokens:**
   - In [`DesktopSession.cs:534-535`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/clients/Gram.Desktop/DesktopSession.cs#L534-L535):
     ```csharp
     var thinking = ExtractThinkingDelta(item);
     if (!string.IsNullOrEmpty(thinking)) DraftThinking += thinking;
     ```
   - In [`DesktopMessageList.razor:186-190`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/clients/Gram.Desktop/Components/CanvasArea/DesktopMessageList.razor#L186-L190):
     ```razor
     <div class="dt-thinking-indicator">
         <span class="dt-thinking-dot"></span>
         <span class="dt-thinking-text">@(!string.IsNullOrWhiteSpace(Session.ActiveStepStatus) ? Session.ActiveStepStatus : (Session.IsThinking ? "Thinking & reasoning..." : ...))</span>
     </div>
     ```
   - **Staff Critique:** Models like DeepSeek R1 and OpenAI o1/o3 produce hundreds of tokens of detailed architectural reasoning. Gram consumes the user's quota to generate this reasoning, stores it in memory, and then hides it behind a 3-word animated spinner. The user is left staring at a spinning dot wondering if the agent is stuck.

2. **Faked Terminal State:**
   - In [`Workspace.razor:152`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Pages/Workspace.razor#L152):
     ```csharp
     private const bool TerminalTabsEnabled = false;
     ```
   - The UI contains full terminal tab models and infosheet panes ([`Workspace.razor:87-92`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Pages/Workspace.razor#L87-L92)), but terminal tabs are hardcoded to `false`, leaving terminal services partially instantiated in memory while disabled in the canvas.

3. **Retrospective Rollback Illusion:**
   - The change review bar ([`WorkspaceChangeReviewBar.razor:39-42`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/clients/Gram.Desktop/Components/CanvasArea/WorkspaceChangeReviewBar.razor#L39-L42)) presents a green "Accept All" and red "Revert" button. This mimics an approval gate, but is actually an *undo* button. The files were already written to disk seconds earlier. If a build was running in another terminal, it already broke.

---

### 3. Coherence of Mental Model: Chat vs. Code vs. Automations

The UI fails to present a singular, coherent mental model:
- **Chat Mode:** Feels like a generic ChatGPT web clone with prompt suggestion chips and an animated cartoon mascot ([`GramCharacter.razor`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Components/GramCharacter.razor)).
- **Code Mode:** Attempts to be a multi-pane IDE with file trees, git review bars, and diff modals, but lacks code editor integration, LSP navigation, or syntax-directed editing.
- **Automation Mode:** Non-existent in the primary workspace UI. Automations are scattered across orphaned routes (`/sandboxes`, `/tools`, `/hub`) with no bridge between chatting with an agent and configuring a cron job or background runner.

---

### 4. Frontend Code Quality & Engineering Hygiene

- **Monolithic Duplicated CSS (464 KB Redundancy):**
  - Exactly identical 232 KB files exist at:
    1. [`Gram.Blazor/wwwroot/desktop.css`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/wwwroot/desktop.css) (237,170 bytes)
    2. [`Gram.Blazor/clients/Gram.Desktop/wwwroot/css/desktop.css`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/clients/Gram.Desktop/wwwroot/css/desktop.css) (237,170 bytes)
  - `diff -u` returns zero differences. Neither file is managed via SCSS/Tailwind build pipelines; they are massive hand-edited CSS files with hundreds of competing overrides.
- **State Management & Blazor Circuit Coupling:**
  - UI state is split unpredictably between Blazor component parameters, singleton services (`TabService`, `LayoutService`), and `DesktopSession`.
  - A WebSocket drop or Blazor Server circuit disconnect wipes active streaming response buffers and forces a full UI reload.
- **Accessibility (a11y) Basics:**
  - Modals lack focus traps (`WorkspacePickerModal.razor`).
  - Streaming message containers lack proper `aria-live="polite"` regions, making screen readers repeatedly stutter or miss streaming updates.

---

### 5. Top 5 UX Flaws Most Likely to Make a New User Quit

1. **Silent File Clobbering Without Pre-Diff Confirmation:**  
   The agent modifies files on the user's disk without showing an upfront diff. If the user had unsaved changes in their editor, they are destroyed.
2. **The "Thinking..." Black Box Stall:**  
   Reasoning models produce no visible text for 30–60 seconds while thinking. Gram shows a single pulsing dot `"Thinking & reasoning..."` without streaming reasoning tokens, causing users to believe the app crashed and refresh the page.
3. **Conversational Scaffolding Rejection:**  
   When a user asks: "Create a new Next.js app with Tailwind", the agent replies with conversational questions instead of writing code because [`GramSystemPrompt.cs:65-66`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/GramSystemPrompt.cs#L65-L66) forbids it from creating files on initial turns.
4. **Context Overflow Crash Dumps:**  
   When conversation history exceeds model limits, the user receives an unformatted red JSON error bubble: `[Error: 400 Bad Request: prompt exceeds maximum context length]`, and the entire conversation becomes permanently stuck.
5. **Orphaned Navigation & Dead Hubs:**  
   Top navigation buttons lead to `/toolbox`, `/skills`, and `/mcp` pages that have no connection to the active chat session, creating a disjointed "hall of mirrors" experience.

---

## Phase 6: Code Quality & Engineering Hygiene

### 1. Structure, Naming, Consistency & Dead Code

#### A. Dead & Orphaned Code Inventory:
1. **[`ModelCapabilityProbeService.cs`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/ModelCapabilityProbeService.cs#L18-L452) (452 lines):** Fully implemented test battery for reasoning, tool calling, and patch accuracy. Never called or referenced by any active component.
2. **[`GramSystemPrompt.BuildSummarizationPrompt`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/GramSystemPrompt.cs#L131-L151) (21 lines):** Sophisticated 5-part summarization instruction. Never called; context compaction relies on `TakeLast(16)`.
3. **Stripped `Gram.Desktop.csproj`:**
   In [`Gram.Desktop.csproj:22-26`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/clients/Gram.Desktop/Gram.Desktop.csproj#L22-L26):
   ```xml
   <ItemGroup>
     <Compile Remove="Desktop*.cs;Services/**/*.cs" />
     <Content Remove="Components/**/*.razor" />
     <None Remove="Components/**/*.razor" />
     <RazorComponent Remove="Components/**/*.razor" />
   </ItemGroup>
   ```
   The project named `Gram.Desktop` excludes all of its own UI components from compilation and has been turned into a headless process runner, while its `Components/` folder still contains 18 active-looking Razor files that are actually compiled by `Gram.Blazor.csproj`.
4. **Quarantined Python AST Engine:**
   [`engine/ast_symbol_extractor.py`](file:///Users/taufeeqali/projects/gram/engine/ast_symbol_extractor.py) contains a full Tree-sitter implementation, but `launch.sh` never runs it.

#### B. Naming Inconsistencies:
- `Gram.Desktop` is actually a console/background runner, while `Gram.Blazor` is the actual desktop web UI.
- `GramBox` is used interchangeably to refer to a Docker container, a transient git worktree (`GramBoxWorktreeManager`), and a local process runner.

---

### 2. "Vibe-Code Smell" List

Specific implementations that demonstrate unplanned, prompt-generated development:

1. **Insecure Predictable Encryption Salt ([`ModelService.cs:529`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/ModelService.cs#L529)):**
   ```csharp
   var salt = Encoding.UTF8.GetBytes(Environment.UserName + Environment.MachineName + "GramSecretKeySalt2026");
   ```
   Derives AES encryption keys for user API credentials using predictable host environment variables.
2. **Zillow / Apartment Search Contamination in System Prompt ([`GramSystemPrompt.cs:38-45`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/GramSystemPrompt.cs#L38-L45)):**
   Production coding agent prompt contains rules for apartment hunting ("Never browse individual unit URLs, always search Zillow").
3. **Fake Reminder Tool ([`AgentTools.cs:2673-2680`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentTools.cs#L2673-L2680)):**
   Tool returns a success string to the LLM claiming a reminder was scheduled, but executes zero background timers or cron jobs.
4. **Magic Unsandboxed Bash Execution ([`AgentTools.cs:1561-1582`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentTools.cs#L1561-L1582)):**
   Launches arbitrary terminal shell strings on the host OS via `Process.Start("/bin/zsh", $"-c \"{command}\"")` with zero path guards or token filtering.
5. **Hardcoded Port Conventions:**
   Hardcoded references to `7337` (Server), `7340` (LocalAgent), and `8787` (Python Engine) scattered across C# source files without centralized configuration.
6. **Cartoon Mascot Avatar in Developer Tool ([`GramCharacter.razor:7-10`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Components/GramCharacter.razor#L7-L10)):**
   308 lines of SVG animations implementing `is-covering-eyes`, `state-jumping`, and `state-wave` for an AI character inside an intended Codex competitor.
7. **Recursive Symlink In Test Output:**
   `Gram.Blazor/tests/` contains deeply nested recursive directories in `bin/Debug/` that cause search tools like `find` to loop infinitely across thousands of redundant directory levels.

---

### 3. Test Coverage & Engineering Discipline

- **Unit Tests: 560 Passing Tests [FINE for Utilities, ZERO for Agent Orchestration]**  
  `dotnet test Gram.Blazor/tests/Gram.Blazor.UnitTests/Gram.Blazor.UnitTests.csproj` executes 560 unit tests in 16 seconds with 100% pass rate.
- **The Critical Testing Blindspot:**
  - 100% of the tests cover standalone helper algorithms: `SafeSqlSanitizer`, `SurgicalPatchEngine`, `CompilerDiagnosticParser`, `MarkdownRenderer`.
  - **Zero tests verify `AgentOrchestrator.RunAgentLoopAsync` end-to-end.**
  - **Zero tests verify multi-turn LLM streaming, token limits, or error recovery.**
  - **Zero SWE-bench or real-world coding benchmark evaluations exist.**

---

### 4. Developer Onboarding & Environment Setup

- **README Accuracy: [LOW]**  
  The documentation describes a unified autonomous coding system, but does not document that `engine/server.py` must be manually started in a separate virtual environment to enable Tree-sitter AST extraction.
- **15-Minute New Developer Run Test: [FAILS]**
  1. Running `dotnet run --project Gram.Server.csproj` starts the server on port 7337.
  2. Opening the UI reveals missing API keys.
  3. Configuring keys requires deciphering undocumented provider IDs.
  4. Running code generation immediately triggers the scaffolding block, leading new developers to believe code writing is broken.

---

## Deliverables

### 1. Executive Verdict (Max 10 Lines)

> **Verdict: Gram is a collection of high-potential, sophisticated experiments that currently lacks the architectural discipline and operational coherence of a single unified product.**
> 
> The codebase contains genuine technical gems—most notably the 4-tier `SurgicalPatchEngine` and the automated compiler self-healing validation loop. However, these are drowned in architectural fragmentation: Anthropic direct API calls are broken via 404 URL construction, the Tree-sitter engine is quarantined in an unlaunched Python sidecar, user credentials use predictable salt encryption, active conversation state is 100% in-memory with zero crash recovery, and the UI oscillates between an IDE, an apartment-hunting chatbot, and a waving cartoon mascot. To become a viable model-agnostic Codex alternative, Gram must aggressively cut ancillary distractions, unify its process boundaries, and harden its core agent execution loop.

---

### 2. Scorecard Table

| Audit Phase | Score (1–10) | One-Line Technical Justification |
| :--- | :---: | :--- |
| **Phase 0: Product Definition & Inventory** | **4.5 / 10** | Fragmented across 4 runtimes with extensive architectural overlap and missing bridges. |
| **Phase 1: System Design & Architecture** | **3.5 / 10** | Zero persistence on active runs, broken Anthropic protocol, unsandboxed host shell execution. |
| **Phase 2: Prompt Structuring** | **4.0 / 10** | Prompts polluted with real-estate instructions, self-sabotaging scaffolding bans, blind `TakeLast(16)` slicing. |
| **Phase 3: Modeling & Model Layer** | **5.0 / 10** | Robust streaming normalization, but single-provider fallback, zero backoff, and orphaned capability probe. |
| **Phase 4: Code Generation Quality** | **7.5 / 10** | Outstanding surgical patch engine and pre-commit self-healing loop, dragged down by regex AST parsing. |
| **Phase 5: UI / UX** | **4.0 / 10** | 232 KB duplicated CSS, hidden thinking tokens, retrospective-only change review, and raw JSON crash dumps. |
| **Phase 6: Code Quality & Hygiene** | **5.0 / 10** | 560 unit tests pass, but zero end-to-end agent tests, dead code blocks, and predictable crypto salts. |
| **OVERALL SYSTEM COHERENCE** | **4.8 / 10** | **Collection of loosely connected experiments requiring aggressive unification.** |

---

### 3. Tagged Findings Summary

| Tag | Phase | Finding & Evidence | Concrete Remediation |
| :--- | :--- | :--- | :--- |
| **[CRITICAL]** | Phase 1 | Direct Anthropic API calls fail with 404 (`AgentOrchestrator.cs:1700`) due to appending `/v1/messages` to `https://api.anthropic.com/v1`. | Normalize base URL in `ModelService.cs` using string stripping before appending endpoint paths. |
| **[CRITICAL]** | Phase 1 | Unsandboxed command execution via host shell (`AgentTools.cs:1561`). Model can run `rm -rf /` or leak secrets. | Enforce command whitelist, path sandboxing via Docker/chroot, and pre-execution user authorization prompts. |
| **[CRITICAL]** | Phase 1 | Predictable encryption key salt (`ModelService.cs:529`) using `UserName + MachineName + "GramSecretKeySalt2026"`. | Migrate to OS-native secure stores: Apple Keychain (`security`), Windows DPAPI, or Linux Secret Service via `libsecret`. |
| **[CRITICAL]** | Phase 4 | File write operations silently overwrite user's uncommitted editor modifications with no mtime check. | Inspect file mtime and git status prior to write; abort and prompt user if file was modified externally. |
| **[CRITICAL]** | Phase 2 | Blind context window slicing (`TakeLast(16)`) produces unhandled HTTP 400 errors and crashes sessions. | Implement token-budget compaction that measures prompt tokens and invokes LLM summarization on overflow. |
| **[HIGH]** | Phase 2 | Prompt pollution with real estate instructions (`GramSystemPrompt.cs:38-45`) and self-sabotaging scaffolding ban (`lines 65-66`). | Strip all non-coding domain instructions; rewrite scaffolding rule into affirmative file-creation directives. |
| **[HIGH]** | Phase 3 | Hardcoded fallback strictly to Groq (`AgentOrchestrator.cs:459-472`); zero exponential backoff or retry for other providers. | Implement generic `IModelFallbackStrategy` with jittered exponential backoff and configurable secondary provider routes. |
| **[HIGH]** | Phase 4 | AST symbol discovery uses brittle regular expressions (`RepositoryMapService.cs:30-48`); Tree-sitter engine is quarantined in offline Python sidecar. | Integrate in-process C# Tree-sitter bindings (`TreeSitter.Bindings`) and deprecate regex scanning. |
| **[HIGH]** | Phase 5 | Reasoning tokens from o1/DeepSeek R1 are captured in `DesktopSession.cs:535` but hidden in `DesktopMessageList.razor:186-190`. | Add an expandable `<details>` reasoning disclosure block in the chat bubble showing live streaming thinking text. |
| **[MEDIUM]** | Phase 1 | Fake reminder tool (`AgentTools.cs:2673-2680`) returns simulated success without scheduling background execution. | Wire tool to a persistent cron/timer runner (e.g. Quartz.NET or system background service) or delete tool. |
| **[MEDIUM]** | Phase 5 | Identical 232 KB CSS files duplicated at `Gram.Blazor/wwwroot` and `Gram.Desktop/wwwroot/css`. | Consolidate styles into a single Razor Class Library asset or SCSS pipeline; remove duplicate static asset. |
| **[LOW]** | Phase 5 | Cartoon mascot avatar (`GramCharacter.razor`, 308 lines) occupies layout space in professional coding tool. | Move mascot to opt-in easter egg or eliminate to streamline developer UI. |

---

### 4. Consolidated "Vibe-Code Smell" List

1. **Insecure Salt Derivation:** [`ModelService.cs:529`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/ModelService.cs#L529) — `Environment.UserName + Environment.MachineName + "GramSecretKeySalt2026"`.
2. **Contaminated System Prompt:** [`GramSystemPrompt.cs:38-45`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/GramSystemPrompt.cs#L38-L45) — Apartment search guidelines inside a software engineering agent.
3. **Ghost Reminder Tool:** [`AgentTools.cs:2673`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentTools.cs#L2673) — Tool returns fake scheduling confirmation with zero backing infrastructure.
4. **Direct Host Shell Execution:** [`AgentTools.cs:1561`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentTools.cs#L1561) — Raw `Process.Start("/bin/zsh")` without escaping or sandbox enforcement.
5. **Hardcoded Port Tiers:** Arbitrary ports `7337`, `7340`, `8787` scattered across disparate C# services.
6. **Cartoon Mascot In Developer Tool:** [`GramCharacter.razor`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Components/GramCharacter.razor) — 308 lines of SVG eye-covering and jumping animations in a Codex alternative.
7. **Recursive Build Symlinks in Tests:** Infinite recursion in `Gram.Blazor/tests/Gram.Blazor.Tests/bin/Debug/net9.0/tests/...`.
8. **Stripped Desktop Csproj:** [`Gram.Desktop.csproj:22-26`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/clients/Gram.Desktop/Gram.Desktop.csproj#L22-L26) excludes its own code and Razor components from compilation.
9. **Dead 452-Line Model Capability Probe:** [`ModelCapabilityProbeService.cs`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/ModelCapabilityProbeService.cs#L18-L452) is fully written but never executed.
10. **Dead Summarization Prompt:** [`GramSystemPrompt.BuildSummarizationPrompt`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/GramSystemPrompt.cs#L131-L151) is constructed but ignored in favor of `TakeLast(16)`.

---

### 5. Recommended Target Architecture & Clean Monorepo Layout

```
                  ┌──────────────────────────────────────────────┐
                  │                 Presentation                 │
                  │  Gram.Desktop (Photino / Blazor Hybrid)      │
                  │  - Monaco Editor Integration                 │
                  │  - Streaming Chat & Collapsible Thinking Log │
                  │  - Pre-Commit Interactive Unified Diff View  │
                  └──────────────────────┬───────────────────────┘
                                         │
                                         ▼ (Local Named Pipe / Domain Socket)
                  ┌──────────────────────────────────────────────┐
                  │               Gram Core Engine               │
                  │               (Local Daemon)                 │
                  │                                              │
                  │  ┌──────────────────┐  ┌──────────────────┐  │
                  │  │ Context & Budget │  │  Agent Loop &    │  │
                  │  │ Token Management │  │  Execution State │  │
                  │  └────────┬─────────┘  └────────┬─────────┘  │
                  │           │                     │            │
                  │           ▼                     ▼            │
                  │  ┌──────────────────┐  ┌──────────────────┐  │
                  │  │ Model Abstraction│  │   Tool Sandbox   │  │
                  │  │  (OpenAI, Anth,  │  │ (Docker/Worktree/│  │
                  │  │   Ollama, Groq)  │  │  Pty Security)   │  │
                  │  └──────────────────┘  └──────────────────┘  │
                  └──────────────────────┬───────────────────────┘
                                         │
             ┌───────────────────────────┴───────────────────────────┐
             ▼                                                       ▼
┌─────────────────────────┐                             ┌─────────────────────────┐
│     SQLite + Vector     │                             │ In-Process Tree-sitter  │
│  State & Run Checkpoint │                             │  AST & Symbol Navigation│
└─────────────────────────┘                             └─────────────────────────┘
```

#### Proposed Consolidated Repository Layout:

```
gram/
├── src/
│   ├── Gram.Core/               # Agent loop, context compaction, state machines, tools
│   │   ├── Abstractions/        # IModelProvider, IToolSandbox, IContextCompactor
│   │   ├── Execution/           # AgentOrchestrator, SurgicalPatchEngine, SelfHealingGate
│   │   ├── Providers/           # OpenAI, Anthropic, Ollama, Groq provider implementations
│   │   ├── Sandbox/             # Native PTY runner, Docker / Worktree isolation
│   │   └── State/               # SQLite persistent checkpoints and session journals
│   ├── Gram.AST/                # In-process C# Tree-sitter bindings (replaces python sidecar)
│   ├── Gram.UI/                 # Consolidated Blazor / Photino desktop frontend
│   │   ├── Components/          # MessageList, CollapsibleThinking, UnifiedDiffViewer
│   │   └── wwwroot/             # Single authoritative CSS bundle
│   └── Gram.CLI/                # Headless command-line tool (terminal agent)
├── tests/
│   ├── Gram.Core.Tests/         # Unit tests for patch engine, parsers, and utilities
│   ├── Gram.IntegrationTests/   # Mock-streaming multi-turn agent loop verification
│   └── Gram.Bench/              # 5-task GramBench standard qualification test harness
└── docs/
    └── audits/                  # Living technical audit reports and roadmaps
```

---

### 6. Prioritized Action Roadmap

#### Next 3 Days: Stabilize & Unify (Stop the Bleeding)
1. **Fix Anthropic Base URL Bug:**
   - *Action:* Modify `ModelService.cs` to ensure `https://api.anthropic.com` does not have duplicate `/v1` segments before sending requests.
   - *Why:* Anthropic models currently fail immediately with HTTP 404.
   - *Done When:* Claude 3.5 Sonnet successfully completes a multi-step coding prompt.
2. **Purge Real Estate Prompt & Fix Scaffolding Rule:**
   - *Action:* Strip Zillow guidelines from `GramSystemPrompt.cs:38-45` and rewrite lines 65-66 to affirmatively permit file creation on initial turns.
   - *Why:* Stops the model from stalling into conversational questions when asked to scaffold projects.
   - *Done When:* Prompting "Create a new hello world console app" immediately calls `write_file`.
3. **Expose Thinking Tokens in UI:**
   - *Action:* Add an expandable `<details>` container in `DesktopMessageList.razor` binding directly to `DraftThinking`.
   - *Why:* Users can see that reasoning models (o1/R1) are actively thinking instead of assuming the app hung.
   - *Done When:* Thinking tokens stream live inside an accordion drawer during reasoning turns.

#### Next 2 Weeks: Core Loop Reliability & Model Abstraction
1. **Implement Token-Budget Context Compactor:**
   - *Action:* Replace `TakeLast(16)` with an active token counter that triggers `BuildSummarizationPrompt` when context exceeds 75% of model limits.
   - *Why:* Eliminates unhandled HTTP 400 context overflow crashes on long agent runs.
   - *Done When:* A 30-turn agent session completes without hitting context length exceptions.
2. **Integrate In-Process Tree-sitter C# Bindings:**
   - *Action:* Replace regex-based `RepositoryMapService` with native C# Tree-sitter bindings, deprecating the unlaunched Python sidecar.
   - *Why:* Provides accurate symbol discovery and code navigation without managing external python venvs.
   - *Done When:* Multi-line methods, decorators, and constructors are correctly indexed in PageRank maps.
3. **Add External File Modification Guards:**
   - *Action:* Add file timestamp (mtime) and git dirty checks to `WorkspaceCheckpointService` before writing to disk.
   - *Why:* Prevents Gram from silently destroying uncommitted manual edits in external IDEs.
   - *Done When:* Modifying a file in VS Code while Gram is executing triggers a conflict resolution prompt.

#### Next 6 Weeks: Evals, UX Hardening & Secure Sandboxing
1. **Activate `GramBench` Automated Benchmark Suite:**
   - *Action:* Wire `ModelCapabilityProbeService` into a CI test runner executing the 5 benchmark tasks (GB-01 through GB-05).
   - *Why:* Objectively measures whether newly added models (or local Ollama models) are capable of autonomous coding before routing tasks to them.
   - *Done When:* Running `dotnet test --filter Category=GramBench` outputs pass/fail scores across tested models.
2. **Implement Interactive Pre-Commit Diff Review:**
   - *Action:* Replace retrospective review bar with an in-editor split/unified diff review modal that requires user approval before flushing edits to disk.
   - *Why:* Brings Gram up to parity with Claude Code and Cursor's safety model.
   - *Done When:* The agent holds file writes in memory until the user clicks "Approve Diff".
3. **Harden Tool Execution Sandboxing:**
   - *Action:* Wrap shell execution in isolated Docker containers or ephemeral git worktrees with strict environment variable isolation.
   - *Why:* Eliminates the risk of arbitrary code execution destroying host machines or leaking secrets.
   - *Done When:* A prompt attempting to access `~/.ssh/id_rsa` or run destructive host commands is blocked by policy.

---

### 7. The "Cut List" (What to Delete or Defer)

To transform Gram into a sharp, credible developer tool, the following bloated or distracting components must be immediately pruned:

1. **Delete Cartoon Mascot Avatar ([`GramCharacter.razor`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Components/GramCharacter.razor)):**
   - *Rationale:* 308 lines of SVG animations (blinking, waving, eye-covering) dilute the product's credibility as a professional engineering tool.
2. **Delete Presentation / Slide Generator ([`HtmlPresentationRenderer.cs`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/HtmlPresentationRenderer.cs)):**
   - *Rationale:* Slide deck generation belongs in an office productivity suite, not an autonomous coding engine. It clutters the system prompt with 35 lines of HTML presentation instructions.
3. **Delete Spotify OAuth & Connector Integrations (`SpotifyOAuthService.cs`):**
   - *Rationale:* Consumer media integrations add zero value to an AI coding tool and add unnecessary attack surface and maintenance overhead.
4. **Delete Offline Python Sidecar (`engine/`):**
   - *Rationale:* Running an unmanaged Python FastAPI server on port 8787 just for Tree-sitter adds fragile multi-runtime dependencies. Replace with native C# Tree-sitter NuGet packages.
5. **Delete Fake Reminder Tool ([`AgentTools.cs:2673`](file:///Users/taufeeqali/projects/gram/Gram.Blazor/Shared/AgentTools.cs#L2673)):**
   - *Rationale:* Fake tools that return simulated success strings without actual execution erode model trust.
6. **Consolidate Duplicated CSS Monoliths:**
   - *Rationale:* Remove `Gram.Blazor/clients/Gram.Desktop/wwwroot/css/desktop.css` and establish a single source of truth for UI styling.

---

### 8. The 5 Questions for the Author

Before investing further engineering capital into this architecture, the following 5 questions must be answered:

1. **What is the single core product identity:** Is Gram an autonomous terminal-based developer tool (like Claude Code), an all-in-one desktop AI IDE (like Cursor), or a general-purpose desktop assistant that plays music and finds apartments?
2. **Why maintain an external Python sidecar for AST parsing** when high-performance, in-process C# Tree-sitter bindings exist directly on NuGet?
3. **What was the architectural rationale for leaving active agent session state 100% in-memory** with zero journal persistence, making runs irrecoverable upon crash?
4. **Why is `Gram.Desktop.csproj` stripped of its own Razor components** while keeping those component files in its directory tree, instead of cleanly separating the project into a class library and host runner?
5. **What is the security boundary model:** Is Gram intended to run completely untrusted third-party prompts and code, and if so, how can that be reconciled with unsandboxed `zsh/bash` host execution and predictable encryption key generation?






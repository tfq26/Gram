# Gram Desktop & Web Launch Guide

Gram runs as a local Bun HTTP/SSE daemon (`src/server/index.ts`, default port `7337`) paired with a React 19 + Vite frontend (`src/ui/`) and an optional Tauri 2 native desktop shell (`src-tauri/`).

## Runtime Architecture

```text
+-------------------------------------------------------+
| Tauri 2 Desktop Window (or any browser)               |
| - Dev: http://127.0.0.1:5174 (Vite HMR)               |
| - Prod: http://127.0.0.1:7337 (dist/studio)           |
+---------------------------+---------------------------+
                            | HTTP / SSE
+---------------------------v---------------------------+
| Bun Daemon (src/server/index.ts, port 7337)           |
| - Agents, tools, SQLite DB, GramFS, process manager   |
+---------------------------+---------------------------+
                            | HTTPS (Bearer token)
+---------------------------v---------------------------+
| Lyra Hub (https://lyra.2helix.org)                    |
| - Better Auth + Google OAuth + /api/gateway/llm/stream|
+-------------------------------------------------------+
```

## Prerequisites

- **All platforms**: [Bun](https://bun.sh) 1.2+ and Git.
- **Desktop (Tauri 2) builds**: [Rust](https://rust-lang.org) (`rustc` and `cargo`) plus platform webview/toolchain requirements:
  - **macOS**: Xcode Command Line Tools (`xcode-select --install`). WKWebView is built into macOS.
  - **Windows 10/11**: Microsoft Edge WebView2 runtime (built into Windows 11) and MSVC C++ Build Tools.
  - **Linux (Ubuntu/Debian)**: `libwebkit2gtk-4.1-dev`, `libgtk-3-dev`, `libayatana-appindicator3-dev`, `build-essential`, `curl`, `pkg-config`.

Install dependencies before first launch:

```bash
bun install
```

## Launching in Development

### Desktop + Live HMR (Recommended for UI/Desktop Work)

Runs `bun run dev` (Bun daemon on `:7337` + Vite HMR on `:5174`) and opens the Tauri 2 window via `src-tauri/tauri.dev.conf.json`:

```bash
bun run tauri:dev
```

### Web UI + Daemon

Run both the daemon (`--watch`) and Vite HMR server without opening a Tauri window:

```bash
bun run dev
```

Then open `http://127.0.0.1:5174`.

### Production Studio Mode (Single Process)

Build the frontend and start the daemon directly on `http://localhost:7337`:

```bash
bun run build:studio
./launch.sh --web
```

Or start headless without opening a browser tab:

```bash
./launch.sh --server
```

### Terminal CLI Mode

Run agent goals or diagnostics straight from the terminal:

```bash
./launch.sh --cli "inspect failing tests"
./launch.sh --doctor
./launch.sh --graph
bun run cli skills
```

## Building Release Artifacts

```bash
# Typecheck + production Vite bundle (outputs to dist/studio/)
bun run build

# Native Rust workspace crate (crates/gram-core-rs)
bun run build:rust

# Standalone Tauri desktop installer/app bundle (reads src-tauri/tauri.conf.json)
bun run tauri:build
```

## Configuration

Copy `.env.example` to `.env` if you need custom ports or overrides:

- `GRAM_PORT`: Change the daemon port (default `7337`).
- `LYRA_HUB_URL`: Point to a local or staging Lyra Hub instead of `https://lyra.2helix.org`.
- `GRAM_DIRECT_LLM=true`: Skip Lyra Hub and talk directly to AWS Bedrock / OpenRouter / local Ollama using local environment credentials.

## Troubleshooting

1. **Port `7337` or `5174` already in use**:
   `vite.config.ts` sets `strictPort: true` on `5174`. Stop lingering processes before restarting:
   ```bash
   lsof -i :7337 -i :5174
   ```
2. **Code changes did not take effect**:
   `./launch.sh --web` and `bun run dev:server` run the compiled `dist/studio/` bundle unless you access `:5174` via Vite or re-run `bun run build:studio`. Restart the Bun daemon after modifying backend files under `src/server/`, `src/runtime/`, or `src/providers/`.
3. **Google OAuth on Desktop**:
   Clicking Google sign-in in Tauri opens your system browser (`https://lyra.2helix.org/api/auth/gram-start`) and hands the session token back via `/api/auth/handoff`. Make sure the local daemon on port `7337` (or `:5174` in dev) matches the origin you opened so `GET /api/auth/handoff-status` receives the completed token.
4. **Debugging model or gateway failures**:
   Check the **Diagnostics** panel on the failed message in the UI, inspect `<workspace>/.gram/diagnostics/errors.jsonl`, or query `http://localhost:7337/api/diagnostics/errors`.

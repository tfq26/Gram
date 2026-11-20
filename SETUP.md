# Gram Setup & Development Guide

For an overview of Gram + Lyra's features, architecture, and roadmap, see [README.md](README.md).

## Prerequisites

- [Bun](https://bun.sh) 1.2+
- [Rust](https://rust-lang.org) (`cargo`) if building the Tauri desktop app or `crates/gram-core-rs`
- Xcode Command Line Tools on macOS (for Tauri native builds)

Install dependencies:

```bash
bun install
```

## Running Gram

### Web + Daemon (Development)

Runs the Bun HTTP/SSE daemon on `http://127.0.0.1:7337` and the Vite dev server on `http://127.0.0.1:5174` (with `/api` proxied to `:7337`):

```bash
bun run dev
```

Or launch only the Bun daemon serving the built frontend (`dist/studio`) on `http://localhost:7337`:

```bash
./launch.sh --web
```

Other launcher modes:

```bash
./launch.sh --server          # Start the HTTP daemon without opening a browser
./launch.sh --cli "prompt"    # Run a single prompt in the terminal
./launch.sh --doctor          # Check local ports, runtime, and environment
./launch.sh --graph           # Index and print sample Semantic AST URIs
```

### Tauri Desktop App

Starts the dev servers (`bun run dev`) and opens the native desktop window pointing at `http://127.0.0.1:5174`:

```bash
bun run tauri:dev
```

Build the production frontend (`dist/studio`) and desktop bundle:

```bash
bun run build
bun run tauri:build
```

For platform prerequisites and troubleshooting, see [docs/desktop-launch-guide.md](docs/desktop-launch-guide.md).

## Checks and Tests

```bash
bun run typecheck    # tsc --noEmit
bun run build:studio # vite build -> dist/studio
bun test             # Bun test suite across tests/ and src/
```

## Environment Variables

See [.env.example](.env.example) for the full template.

| Variable | Default | Purpose |
| :--- | :--- | :--- |
| `GRAM_PORT` | `7337` | Port for the local Bun HTTP/SSE server |
| `LYRA_HUB_URL` | `https://lyra.2helix.org` | Lyra Hub origin for auth and `/api/gateway/llm/stream` |
| `LYRA_GATEWAY_API_KEY` | unset | Optional headless API key for gateway calls without a user session |
| `GRAM_DIRECT_LLM` | unset (`false`) | Set to `true` to bypass Lyra Gateway and call Bedrock/OpenRouter directly |
| `OLLAMA_BASE_URL` | `http://host.docker.internal:11434` | Local Ollama endpoint for `local-only` or BYOK models |
| `OPENROUTER_API_KEY` | unset | Optional OpenRouter key when calling OpenRouter directly |
| `PARALLEL_API_KEY` / `TAVILY_API_KEY` | unset | Optional web search providers (falls back to DuckDuckGo HTML search) |
| `ASSEMBLYAI_API_KEY` / `ELEVENLABS_API_KEY` | unset | Optional server-side speech-to-text and text-to-speech |

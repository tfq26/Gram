# Gram Desktop

Gram Desktop is a native cross-platform desktop client for Gram built with .NET 9 and Photino.Blazor, designed with a clean, disciplined Carbon dark workspace aesthetic.

## Quick Start (macOS)

### 1. Interactive One-Click Launch (Recommended)
Double-click **`Launch Gram Desktop.command`** in the repository root, or run:

```bash
./scripts/launch-desktop.sh
```

This script automatically verifies the .NET SDK, detects whether the Gram backend is running on `http://localhost:7337` (starting it in the background if needed), and opens the native GUI window.

To spawn the launcher directly into a dedicated native macOS Terminal window:
```bash
./scripts/open-in-terminal.sh
```

### 2. Headless CLI / Subagent Automation Mode
When running in headless CI or subagent scripts without a display:

```bash
./scripts/launch-desktop.sh --cli "What models are available?"
```
or:
```bash
dotnet run --project clients/Gram.Desktop -- --backend http://localhost:7337 --message "Say hello"
```

### Optional Arguments:
- `--backend <url>` or `GRAM_BACKEND_URL`: Custom backend endpoint (defaults to `http://localhost:7337`).
- `--token <access-token>` or `GRAM_ACCESS_TOKEN`: Bearer authorization token.
- `--conversation <id>`: Resume an existing session.
- `--model <model-id>`: Target a specific model engine.
- `--workspace <path>`: Explicit local workspace for coding capabilities.

See [docs/desktop-launch-guide.md](../../docs/desktop-launch-guide.md) for full architecture and launch instructions.

Coding capabilities are enabled only for localhost backends and are scoped to the
explicit workspace path. Remote backends never receive local filesystem or terminal
access from the desktop client.

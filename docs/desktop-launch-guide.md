# Gram Desktop Client — Launch Guide

This guide explains how to launch and run the native macOS desktop client (`Gram.Desktop`).

---

## Why Background Agent Shells Cannot Open GUI Windows

On macOS, native desktop applications (like Photino/AppKit/WebKit) require an **active interactive Cocoa WindowServer session (`NSApplication`)** with Mach port access to your active desktop display.

When an AI coding agent or remote script executes commands inside a headless subshell or non-interactive background process (such as a background daemon, SSH, or CI runner), macOS does not attach the process to the window manager, causing `Photino.Blazor` to exit immediately after rendering the initial batch.

To run the native GUI window, the process must be invoked either:
1. Directly in an interactive Terminal window on your Mac, or
2. Via macOS AppleScript (`osascript`) which directs the native macOS `Terminal.app` to host the window.

---

## The 3 Ways to Launch

### Method 1: Double-Click from Finder (Easiest)

In macOS Finder, navigate to the `Gram` project folder and double-click:

📁 **`Launch Gram Desktop.command`**

*This automatically opens a dedicated Terminal session, verifies the backend, and launches the native macOS desktop app.*

---

### Method 2: From Terminal (Interactive Shell)

Open your standard macOS Terminal (or iTerm / VS Code integrated terminal) and run:

```bash
./scripts/launch-desktop.sh
```

Or to spawn a fresh dedicated Terminal window from any background agent or script:

```bash
./scripts/open-in-terminal.sh
```

#### What `launch-desktop.sh` does automatically:
1. Verifies `.NET 9 SDK` is installed.
2. Checks if the Gram backend is running on `http://localhost:7337`.
3. If the backend is not running, it automatically starts `Gram.Blazor` in the background and polls `/api/v1/models` until healthy.
4. Launches `Gram.Desktop` in the foreground.
5. On `Ctrl+C`, cleanly terminates any background backend processes it spawned.

---

### Method 3: Headless CLI Mode (For Automated Agents & CI)

If another AI agent or CI pipeline wants to test prompt responses, verify backend connectivity, or test streaming without spawning a GUI window:

```bash
./scripts/launch-desktop.sh --cli "What models are available?"
```

Or using `dotnet run` directly:

```bash
dotnet run --project Gram.Blazor/clients/Gram.Desktop/Gram.Desktop.csproj -- --backend http://localhost:7337 --message "Ping"
```

---

## Manual Step-by-Step Commands

If you prefer to start each component manually:

### Step 1: Start Backend API
```bash
dotnet run --project Gram.Blazor/Gram.Blazor.csproj
```
*(Wait until you see `Now listening on: http://0.0.0.0:7337`)*

### Step 2: In a separate Terminal window, start Desktop Client
```bash
dotnet run --project Gram.Blazor/clients/Gram.Desktop/Gram.Desktop.csproj
```

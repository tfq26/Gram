# Gram Desktop Client — Multi-Platform Launch Guide

Gram Desktop (`Gram.Desktop`) is a high-performance native desktop shell built with **.NET 9** and **Photino.Blazor**. It runs natively across **Windows**, **macOS**, and **Linux** without the memory overhead of Electron, using the platform's native webview component directly.

---

## 1. Architecture Overview

Gram operates as a dual-component or unified client-server architecture:

```
┌─────────────────────────────────────────────────────────────┐
│                  Gram Desktop GUI Shell                     │
│               (Gram.Blazor/clients/Gram.Desktop)            │
│                                                             │
│   Windows: Edge WebView2  │  macOS: WKWebView  │  Linux: WebKitGTK   │
└──────────────────────────────┬──────────────────────────────┘
                               │ HTTP / SSE (Port 7337)
┌──────────────────────────────▼──────────────────────────────┐
│                    Gram Core Backend                        │
│                   (Gram.Blazor/Gram.Blazor)                 │
│                                                             │
│  Models · Streaming · Git Tooling · Workspace FS · Sandbox  │
└─────────────────────────────────────────────────────────────┘
```

- **Backend API**: ASP.NET Core server listening by default on `http://localhost:7337`.
- **Desktop Host**: Native window container powered by Photino.
- **PTY Terminal**: Cross-platform pseudoterminal integration (ConPTY on Windows, POSIX PTY on macOS/Linux).
- **Automation Scripts**: Automatically verify prerequisites, spawn the backend in the background if not running, wait for healthiness, and launch the GUI.

---

## 2. Platform Prerequisites

### Common Prerequisites (All Platforms)
- **.NET 9 SDK**: Ensure `dotnet --version` outputs `9.0.x` or higher.  
  Download: [https://dotnet.microsoft.com/download/dotnet/9.0](https://dotnet.microsoft.com/download/dotnet/9.0)
- **Git**: Installed and accessible in your system `PATH`.

---

### Windows (10 / 11)
- **Microsoft Edge WebView2 Evergreen Runtime**:
  - Pre-installed by default on Windows 11 and up-to-date Windows 10 installations.
  - If missing on older Windows machines, install via: [Microsoft WebView2 Download](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)
- **Terminal Shell (Optional but Recommended)**:
  - Gram's embedded terminal defaults to **PowerShell 7** (`pwsh.exe`) if detected in `Program Files`, otherwise falling back to standard `cmd.exe`.
  - Install PowerShell 7 via `winget install Microsoft.PowerShell`.

---

### macOS (Apple Silicon & Intel)
- **macOS 12.0+** (Monterey, Ventura, Sonoma, Sequoia).
- Apple WebKit is built into macOS — no additional runtime download required.
- **Interactive WindowServer Requirement**:
  > [!NOTE]
  > On macOS, native GUI windows (`NSApplication`) require an active interactive Cocoa WindowServer session with Mach port access to the screen. If launched from a headless background subshell or CI runner, Photino will exit after the first render. Run commands in an interactive Terminal window or use `Launch Gram Desktop.command`.

---

### Linux (Ubuntu, Debian, Fedora, Arch)
- **Display Server**: X11 or Wayland (via XWayland).
- **WebKit2GTK and AppIndicator Libraries**:
  - **Ubuntu 22.04+ / Debian 12+**:
    ```bash
    sudo apt update
    sudo apt install -y libwebkit2gtk-4.1-0 libayatana-appindicator3-1 curl
    ```
    *(For older Ubuntu 20.04 distributions, install `libwebkit2gtk-4.0-37 libappindicator3-1`)*
  - **Fedora 38+ / RHEL 9+**:
    ```bash
    sudo dnf install -y webkit2gtk4.1 libappindicator-gtk3 curl
    ```
  - **Arch Linux / Manjaro**:
    ```bash
    sudo pacman -S --noconfirm webkit2gtk-4.1 libappindicator-gtk3 curl
    ```

---

## 3. How to Launch on Windows

### Method 1: Double-Click Launcher (Easiest)
In Windows File Explorer, navigate to the `Gram` folder and double-click:

📄 **`Launch Gram Desktop.bat`**

This starts a background backend server (if needed) and opens the native Windows window.

### Method 2: PowerShell Script
Open Windows Terminal or PowerShell in the `Gram` folder:

```powershell
.\scripts\launch-desktop.ps1
```

If script execution is disabled on your system, bypass it with:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\launch-desktop.ps1
```

### Method 3: Manual Two-Terminal Launch
If you prefer running the backend and desktop separately to see live backend logs:

**Terminal 1 (Backend):**
```powershell
dotnet run --project Gram.Blazor\Gram.Blazor.csproj
```
*(Wait until `Now listening on: http://0.0.0.0:7337` appears)*

**Terminal 2 (Desktop Client):**
```powershell
dotnet run --project Gram.Blazor\clients\Gram.Desktop\Gram.Desktop.csproj
```

---

## 4. How to Launch on macOS

### Method 1: Double-Click from Finder
In Finder, open the `Gram` folder and double-click:

📁 **`Launch Gram Desktop.command`**

### Method 2: Terminal Interactive Shell
Open Terminal, iTerm2, or VS Code terminal:

```bash
./scripts/launch-desktop.sh
```

To spawn a dedicated fresh Terminal window programmatically:
```bash
./scripts/open-in-terminal.sh
```

### Method 3: Native macOS Application Bundle (`Gram.app`)
To build and run an `.app` bundle with custom app icon:
```bash
./scripts/bundle-macos-app.sh
open Gram.app
```

### Method 4: Manual Two-Terminal Launch
**Terminal 1 (Backend):**
```bash
dotnet run --project Gram.Blazor/Gram.Blazor.csproj
```

**Terminal 2 (Desktop Client):**
```bash
dotnet run --project Gram.Blazor/clients/Gram.Desktop/Gram.Desktop.csproj
```

---

## 5. How to Launch on Linux

### Method 1: Automated Script Launcher
Open your terminal in the `Gram` folder:

```bash
./scripts/launch-desktop.sh
```

The script automatically detects Linux, checks your `.NET 9 SDK`, checks if the backend is running, boots it in the background if necessary, and opens the native GTK3/WebKit window.

### Method 2: Manual Launch
**Terminal 1 (Backend):**
```bash
dotnet run --project Gram.Blazor/Gram.Blazor.csproj
```

**Terminal 2 (Desktop Client):**
```bash
dotnet run --project Gram.Blazor/clients/Gram.Desktop/Gram.Desktop.csproj
```

> [!TIP]
> **Wayland Note**: If your desktop uses pure Wayland and the window does not display, run with XWayland compatibility:
> ```bash
> GDK_BACKEND=x11 dotnet run --project Gram.Blazor/clients/Gram.Desktop/Gram.Desktop.csproj
> ```

---

## 6. Headless CLI Mode (Automation & CI Verification)

You can run prompts and test backend connectivity directly from the command line without opening a GUI window on any platform.

### macOS & Linux:
```bash
./scripts/launch-desktop.sh --cli "What models are configured?"
```

### Windows (PowerShell):
```powershell
.\scripts\launch-desktop.ps1 -Cli -Message "What models are configured?"
```

### Direct `dotnet run` (All Platforms):
```bash
dotnet run --project Gram.Blazor/clients/Gram.Desktop/Gram.Desktop.csproj -- --backend http://localhost:7337 --message "Ping"
```

---

## 7. Building Standalone Release Executables

To build self-contained or framework-dependent production binaries for distribution:

### Windows x64:
```powershell
dotnet publish Gram.Blazor\clients\Gram.Desktop\Gram.Desktop.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o .\dist\win-x64
```
*Output: `dist\win-x64\Gram.Desktop.exe`*

### macOS (Apple Silicon ARM64):
```bash
dotnet publish Gram.Blazor/clients/Gram.Desktop/Gram.Desktop.csproj \
  -c Release \
  -r osx-arm64 \
  --self-contained false \
  -o ./dist/osx-arm64
```
*Or run `./scripts/bundle-macos-app.sh` to package into a notarizable `Gram.app`.*

### macOS (Intel x64):
```bash
dotnet publish Gram.Blazor/clients/Gram.Desktop/Gram.Desktop.csproj \
  -c Release \
  -r osx-x64 \
  --self-contained false \
  -o ./dist/osx-x64
```

### Linux x64:
```bash
dotnet publish Gram.Blazor/clients/Gram.Desktop/Gram.Desktop.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained false \
  -o ./dist/linux-x64
```
*Output: `dist/linux-x64/Gram.Desktop` (make executable with `chmod +x`)*

### Linux ARM64:
```bash
dotnet publish Gram.Blazor/clients/Gram.Desktop/Gram.Desktop.csproj \
  -c Release \
  -r linux-arm64 \
  --self-contained false \
  -o ./dist/linux-arm64
```

---

## 8. Platform Behavior & Shortcuts Matrix

| Feature / Shortcut | Windows | macOS | Linux |
| :--- | :--- | :--- | :--- |
| **Submit Message** | `Ctrl + Enter` | `⌘ + Enter` | `Ctrl + Enter` |
| **Command Palette** | `Ctrl + K` | `⌘ + K` | `Ctrl + K` |
| **Embedded Shell** | PowerShell 7 / CMD (`pwsh.exe`) | zsh / bash (`/bin/zsh`) | bash / zsh (`$SHELL`) |
| **Rendering Engine** | Microsoft Edge WebView2 | Apple WebKit | WebKit2GTK |
| **Window State Memory** | `%APPDATA%/Gram` | `~/Library/Application Support/Gram` | `~/.config/Gram` |
| **Logs Location** | `.logs\backend.log` | `.logs/backend.log` | `.logs/backend.log` |

---

## 9. Troubleshooting

### Windows Issues

1. **"The type initializer for 'PhotinoNET.PhotinoWindow' threw an exception"**:
   - WebView2 runtime is missing or corrupted.
   - Solution: Download and run the [Evergreen Bootstrapper](https://go.microsoft.com/fwlink/p/?LinkId=2124703).

2. **PowerShell execution policy error**:
   - `File ... cannot be loaded because running scripts is disabled on this system.`
   - Solution: Use `Launch Gram Desktop.bat`, or run `Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned`.

3. **Firewall alert on first run**:
   - Windows Defender Firewall may ask for permission for `dotnet.exe` or `Gram.Blazor.exe` to accept local socket connections on port `7337`.
   - Solution: Click **Allow access** (Private networks).

---

### macOS Issues

1. **Window closes immediately / Photino exits after one render**:
   - Cause: Script was executed from an SSH session, daemon, or non-interactive agent subshell.
   - Solution: Double-click `Launch Gram Desktop.command` in Finder or run `./scripts/launch-desktop.sh` inside an interactive `Terminal.app` window.

2. **"Gram.app is damaged and cannot be opened" (Gatekeeper)**:
   - For locally bundled `.app` files that haven't been signed with an Apple Developer certificate:
     ```bash
     xattr -cr Gram.app
     ```

---

### Linux Issues

1. **"Unable to load shared library 'libwebkit2gtk-4.1.so.0'"**:
   - Cause: WebKitGTK library is not installed.
   - Solution:
     ```bash
     # Ubuntu / Debian
     sudo apt install -y libwebkit2gtk-4.1-0
     # Fedora
     sudo dnf install -y webkit2gtk4.1
     # Arch
     sudo pacman -S webkit2gtk-4.1
     ```

2. **Black or blank window under Wayland**:
   - Force X11 backend fallback:
     ```bash
     GDK_BACKEND=x11 ./scripts/launch-desktop.sh
     ```

3. **Missing system tray or indicator library**:
   - Install `libayatana-appindicator3-1` (Debian/Ubuntu) or `libappindicator-gtk3` (Fedora/Arch).

---

### General Backend Connectivity Issues

If the desktop client loads but says `Backend Offline`:
1. Check if the backend is listening:
   ```bash
   curl http://localhost:7337/api/v1/models
   ```
2. Inspect log files:
   - Backend log: `.logs/backend.log`
   - Desktop log: `.logs/desktop.log`
   - Local Agent log: `.logs/local-agent.log`
3. Specify a custom backend URL if running on a non-standard port:
   ```bash
   dotnet run --project Gram.Blazor/clients/Gram.Desktop/Gram.Desktop.csproj -- --backend http://localhost:8080
   ```

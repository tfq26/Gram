# Gram Desktop & Web Client — Multi-Platform Launch Guide

Gram provides multiple native and web presentation surfaces built with **.NET 10**:
1. **Gram.Maui** (Desktop): High-performance native desktop shell for **Windows** (WinUI 3 / Edge WebView2) and **macOS** (Mac Catalyst / WKWebView) using .NET MAUI Blazor Hybrid.
2. **Gram.Blazor** (Web): Full-featured web application hosting both the **Classic Web UI** (at `/` and `/classic`) and the modern **Carbon 3-Pane Workspace UI** (at `/workspace`) for side-by-side comparison.

---

## 1. Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│             Gram Presentation Tier (Clients)                │
│                                                             │
│  • Gram.Maui (Windows WinUI 3 & macOS Mac Catalyst)         │
│  • Gram.Blazor Web (Modern Workspace at /workspace)         │
│  • Gram.Blazor Web (Classic Web UI at /classic & /)         │
└──────────────────────────────┬──────────────────────────────┘
                               │ HTTP / SSE / WebSockets (Port 7337)
┌──────────────────────────────▼──────────────────────────────┐
│                    Gram Core Backend                        │
│                   (Gram.Blazor/Gram.Blazor)                 │
│                                                             │
│  Models · Streaming · Git Tooling · Workspace FS · Sandbox  │
└─────────────────────────────────────────────────────────────┘
```

- **Backend API**: ASP.NET Core server listening by default on `http://localhost:7337`.
- **Desktop Host (`Gram.Maui`)**: Native window container powered by .NET MAUI Blazor Hybrid.
- **Web Host (`Gram.Blazor`)**: Interactive server-rendered Blazor application serving both modern and classic workspace interfaces.
- **PTY Terminal**: Cross-platform pseudoterminal integration (ConPTY on Windows, POSIX PTY on macOS).
- **Automation Scripts**: Automatically verify prerequisites, spawn the backend in the background if not running, wait for healthiness, and launch the GUI.

---

## 2. Platform Prerequisites

### Common Prerequisites (All Platforms)
- **.NET 10 SDK**: Ensure `dotnet --version` outputs `10.0.x` or higher.
  Download: [https://dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Git**: Installed and accessible in your system `PATH`.
- **MAUI workload**: Install with `dotnet workload install maui-maccatalyst` on macOS or `dotnet workload install maui-windows` on Windows.

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
- Requires Xcode command line tools or full Xcode for compilation.

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

Choose a mode with the PowerShell launcher:

```powershell
.\scripts\launch-desktop.ps1 -Desktop
.\scripts\launch-desktop.ps1 -Web
```

The desktop mode is the default when no switch is provided.

### Method 1: Double-Click Launcher (Easiest)
In Windows File Explorer, navigate to the `Gram` folder and double-click:

**`Launch Gram Desktop.bat`**

This starts a background backend server (if needed) and opens the native Windows window.

### Method 2: PowerShell Script
Open Windows Terminal or PowerShell in the `Gram` folder:

```powershell
.\scripts\launch-desktop.ps1 -Desktop
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

**Terminal 2 (Desktop Client - Windows MAUI):**
```powershell
dotnet run --project Gram.Blazor\clients\Gram.Maui\Gram.Maui.csproj -f net10.0-windows10.0.19041.0
```

---

## 4. How to Launch on macOS

Choose either app from the unified launcher:

```bash
./launch.sh --desktop
./launch.sh --web
```

With no mode flag, macOS starts the desktop app.

### Method 1: Double-Click from Finder
In Finder, open the `Gram` folder and double-click:

**`Launch Gram Desktop.command`**

### Method 2: Terminal Interactive Shell
Open Terminal, iTerm2, or VS Code terminal:

```bash
./launch.sh --desktop
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

**Terminal 2 (Desktop Client - MacCatalyst MAUI):**
```bash
dotnet run --project Gram.Blazor/clients/Gram.Maui/Gram.Maui.csproj -f net10.0-maccatalyst27.0
```

---

## 5. Web Interface Access (Dual UI)

Gram.Blazor provides both modern Carbon Workspace and Classic web UIs simultaneously:
- **Modern Carbon Workspace UI**: `http://localhost:7337/workspace`
- **Classic Web UI**: `http://localhost:7337/classic` (or `http://localhost:7337/`)
- A switcher bar at the top of each web page allows one-click switching between views.

### Method 1: Automated Script Launcher
Open your terminal in the `Gram` folder:

```bash
./launch.sh --web
```

On Linux, the script automatically checks your `.NET 10 SDK`, starts the background backend if necessary, and opens the Web UI in your default browser.

### Method 2: Manual Backend Launch
```bash
dotnet run --project Gram.Blazor/Gram.Server/Gram.Server.csproj
```
Then navigate to `http://localhost:7337/workspace` in any modern web browser.

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

To build production binaries for the native MAUI desktop application:

### macOS Application Bundle (`Gram.app`):
```bash
./scripts/bundle-macos-app.sh Release
```
*Output: `Gram.app` in the repository root. Launch via `open Gram.app`.*

### Windows x64 (WinUI 3):
```powershell
dotnet publish Gram.Blazor\clients\Gram.Maui\Gram.Maui.csproj `
  -f net10.0-windows10.0.19041.0 `
  -c Release `
  -p:WindowsPackageType=None
```
*Output: `Gram.Blazor\clients\Gram.Maui\bin\Release\net10.0-windows10.0.19041.0\win10-x64\publish\Gram.Maui.exe`*

---

## 8. Platform Behavior & Shortcuts Matrix

| Feature / Shortcut | Windows | macOS | Web / Linux |
| :--- | :--- | :--- | :--- |
| **Submit Message** | `Ctrl + Enter` | `⌘ + Enter` | `Ctrl + Enter` |
| **Command Palette** | `Ctrl + K` | `⌘ + K` | `Ctrl + K` |
| **Embedded Shell** | PowerShell 7 / CMD (`pwsh.exe`) | zsh / bash (`/bin/zsh`) | Server host shell |
| **Rendering Engine** | Microsoft Edge WebView2 (WinUI 3) | Apple WebKit (Mac Catalyst) | Host Browser |
| **Window State Memory** | `%APPDATA%/Gram` | `~/Library/Application Support/Gram` | Browser LocalStorage |
| **Logs Location** | `.logs\backend.log` | `.logs/backend.log` | `.logs/backend.log` |

---

## 9. Troubleshooting

### Windows Issues

1. **WebView2 Runtime**:
   - WinUI 3 requires Microsoft Edge WebView2 Evergreen runtime.
   - Pre-installed on Windows 11 and recent Windows 10. If missing: [Microsoft WebView2 Download](https://developer.microsoft.com/en-us/microsoft-edge/webview2/).

2. **PowerShell execution policy error**:
   - `File ... cannot be loaded because running scripts is disabled on this system.`
   - Solution: Use `Launch Gram Desktop.bat`, or run `Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned`.

3. **Firewall alert on first run**:
   - Windows Defender Firewall may ask for permission for `dotnet.exe` to accept local socket connections on port `7337`.
   - Solution: Click **Allow access** (Private networks).

---

### macOS Issues

1. **"Gram.app is damaged and cannot be opened" (Gatekeeper)**:
   - For locally bundled `.app` files that haven't been signed with an Apple Developer certificate:
     ```bash
     xattr -cr Gram.app
     ```

2. **Xcode preview / SDK warnings**:
   - If running against newer macOS previews, ensure the full Xcode application is active:
     ```bash
     sudo xcode-select --switch /Applications/Xcode.app/Contents/Developer
     ```

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

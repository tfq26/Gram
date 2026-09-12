# Gram

Gram is a lightweight, Windows-first terminal coding assistant with a reactive Vocaloid-inspired interface.

The first slice is intentionally small:

- Runs from PowerShell with Python 3.11+.
- Has no required third-party dependencies.
- Provides a reactive terminal avatar and audio-level visualizer.
- Includes a dependency-free FFT spectrum visualizer with 24 frequency bands.
- Supports optional Windows system-audio loopback through `soundcard`.
- Supports typed commands now, with an optional microphone sampler.
- Keeps the model connection behind an OpenAI-compatible HTTP adapter.

## Run it

```powershell
cd C:\Users\taufe\Projects\Gram
py -m gram
```

Try these commands inside the app:

```text
/demo       # animate the avatar and audio meter
/voice      # listen through Windows Speech Recognition
/listen     # enter a prompt as a simulated voice utterance
/model set X # save a model name
/models # show installed Ollama models
/endpoint set X # save an OpenAI-compatible endpoint
/provider openrouter # use OpenRouter
/allowlist add provider/model # permit a specific model
/allowlist show # show permitted models
/audio system # select system-audio visualization on next launch
/model      # show the configured model endpoint
/help
/quit
```

The primary interface is numeric:

```text
1  Listen
2  Type
3  Swap model
4  Help
5  Settings
6  Quit
```

Inside Settings, remap a button with entries such as `1=type` or `3=model`. Type `back` to return. Slash commands remain available as a fallback.

The default model mode is safe preview mode: it displays what would be sent to the coding model without making changes to files or running commands.

Gram introduces itself and reads model responses aloud using Windows SAPI. `/voice` uses the Windows Speech Recognition engine when available. If recognition is unavailable, `/listen` remains the typed fallback.

## Optional model connection

Set an OpenAI-compatible endpoint and API key in PowerShell:

```powershell
$env:GRAM_API_URL = "https://api.openai.com/v1/chat/completions"
$env:GRAM_API_KEY = "..."
$env:GRAM_MODEL = "gpt-4o-mini"
py -m gram
```

Any compatible local endpoint can be used, including an Ollama-compatible gateway. If no endpoint is configured, Gram stays in local preview mode.

Model settings can also be saved in `%APPDATA%\Gram\config.json`:

```json
{
  "api_url": "http://localhost:11434/v1/chat/completions",
  "api_key": "",
  "model": "qwen3.5:9b"
}
```

## Optional microphone level input

The TUI works without audio packages. To make the meter react to a real microphone, install `sounddevice`:

```powershell
py -m pip install sounddevice
py -m gram
```

For Windows system audio instead of microphone input:

```powershell
py -m pip install soundcard numpy
$env:GRAM_AUDIO_SOURCE = "system"
py -m gram
```

Gram defaults to Ollama's native `http://localhost:11434/api/chat` endpoint with `qwen3.5:9b`. Install/pull it separately with `ollama run qwen3.5:9b`, then start Gram. The model can be changed from button `3` or Settings. OpenRouter uses its separate OpenAI-compatible endpoint.

Use `/models` inside Gram to query Ollama's local registry. Gram checks the selected model before sending a request and reports the installed names if it is missing.

Diagnostics are written to `%APPDATA%\Gram\logs\gram.log` with rotating backups. Logs include audio lifecycle, speech-recognition results and confidence, transcript normalization, API requests/responses, model allowlist decisions, and approved command execution. API keys are redacted. Voice transcripts are logged for debugging, so protect or remove log files if they contain sensitive material.

OpenRouter is supported as an explicit provider, but Gram only sends requests to models in its allowlist. Example:

```text
/provider openrouter
/allowlist add qwen/qwen3.5-9b
/model set qwen/qwen3.5-9b
```

The allowlist is stored in `%APPDATA%\Gram\config.json`; an empty allowlist is never permitted.

Model responses may contain PowerShell code blocks. Gram displays each command and asks for approval before running it in the current project directory. It never auto-executes model commands by default.

In Settings, `input` and `output` enumerate audio devices when `sounddevice` is installed. `test` listens once and repeats the recognized text aloud, which is useful for diagnosing transcription quality. Output routing uses the selected device when `sounddevice` and `numpy` are installed; otherwise Windows' default speech output is used.

Speech-to-text is deliberately kept behind the same event interface. The next implementation can add a local Whisper/Vosk adapter without changing the TUI or agent layer.

## Design direction

```text
audio capture -> VAD/acoustic events -> streaming ASR -> correction buffer
             -> intent preview -> model/terminal agent -> spoken response
```

The UI currently exposes the event states needed by that pipeline: idle, listening, thinking, speaking, and warning.

## Lightweight Blazor UI & Native Desktop App

Gram features both a lightweight web interface and a fast, native desktop application (`Gram.Desktop`) built on .NET 9 and Photino (Microsoft Edge WebView2 on Windows, Apple WebKit on macOS, and WebKitGTK on Linux).

### Launching the Desktop Client

- **Windows**: Double-click `Launch Gram Desktop.bat` or run `.\scripts\launch-desktop.ps1`
- **macOS**: Double-click `Launch Gram Desktop.command` or run `./scripts/launch-desktop.sh`
- **Linux**: Run `./scripts/launch-desktop.sh`

For comprehensive platform requirements, building standalone executables, and troubleshooting across Windows, macOS, and Linux, see the [Multi-Platform Desktop Launch Guide](docs/desktop-launch-guide.md).

### Web UI Server
Run the standalone web backend with:

```powershell
cd Gram.Blazor
dotnet run
```

Then open `http://localhost:7337`. It includes conversation return tabs, a responsive chat surface, searchable settings groups, MCP settings, and latest-log access.


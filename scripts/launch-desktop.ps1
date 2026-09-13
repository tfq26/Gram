# ==============================================================================
# Gram Desktop Client Launcher (Windows PowerShell)
# Starts backend server (if not already running) and launches Gram.Desktop
# ==============================================================================

param (
    [string]$BackendUrl = "http://127.0.0.1:7337",
    [string]$LocalAgentUrl = "http://127.0.0.1:7340",
    [switch]$Cli,
    [string]$Message = ""
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir

$BackendProcess = $null
$LocalAgentProcess = $null

function Cleanup {
    if ($BackendProcess -and -not $BackendProcess.HasExited) {
        Write-Host "`nStopping background Gram backend (PID: $($BackendProcess.Id))..." -ForegroundColor Yellow
        Stop-Process -Id $BackendProcess.Id -Force -ErrorAction SilentlyContinue
    }
    if ($LocalAgentProcess -and -not $LocalAgentProcess.HasExited) {
        Write-Host "Stopping background Gram local agent (PID: $($LocalAgentProcess.Id))..." -ForegroundColor Yellow
        Stop-Process -Id $LocalAgentProcess.Id -Force -ErrorAction SilentlyContinue
    }
}

try {
    Write-Host "===============================================" -ForegroundColor Cyan
    Write-Host "       Gram Desktop Native Client Launcher     " -ForegroundColor Cyan
    Write-Host "===============================================" -ForegroundColor Cyan

    # 1. Verify dotnet SDK
    if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
        Write-Host "Error: .NET SDK (dotnet) is not installed or not in PATH." -ForegroundColor Red
        Write-Host "Please install .NET 9 SDK: https://dotnet.microsoft.com/download"
        exit 1
    }

    $LogsDir = Join-Path $RootDir ".logs"
    if (-not (Test-Path $LogsDir)) {
        New-Item -ItemType Directory -Path $LogsDir | Out-Null
    }

    $DotnetVersion = dotnet --version
    Write-Host "Using .NET SDK: $DotnetVersion" -ForegroundColor Green
    Write-Host "Real-time logs: $LogsDir\backend.log" -ForegroundColor Cyan

    # 2. Check or launch Gram Backend
    Write-Host "Checking Gram backend at $BackendUrl..."
    $backendHealthy = $false
    try {
        $res = Invoke-WebRequest -Uri "$BackendUrl/api/v1/models" -Method Get -TimeoutSec 2 -UseBasicParsing -ErrorAction Stop
        if ($res.StatusCode -eq 200) { $backendHealthy = $true }
    } catch {
        $backendHealthy = $false
    }

    if ($backendHealthy) {
        Write-Host "Running." -ForegroundColor Green
    } else {
        Write-Host "Not running. Starting backend in background..." -ForegroundColor Yellow
        $BackendLog = Join-Path $LogsDir "backend.log"
        $BackendErrLog = Join-Path $LogsDir "backend.err.log"
        $BackendProcess = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--project", "$RootDir\Gram.Blazor\Gram.Blazor.csproj") -WorkingDirectory $RootDir -RedirectStandardOutput $BackendLog -RedirectStandardError $BackendErrLog -NoNewWindow -PassThru
        Write-Host "Backend process launched (PID: $($BackendProcess.Id)). Logging to $BackendLog"

        $retries = 45
        $ready = $false
        for ($i = 1; $i -le $retries; $i++) {
            Start-Sleep -Milliseconds 800
            try {
                $check = Invoke-WebRequest -Uri "$BackendUrl/api/v1/models" -Method Get -TimeoutSec 1 -UseBasicParsing -ErrorAction Stop
                if ($check.StatusCode -eq 200) {
                    $ready = $true
                    break
                }
            } catch {}
        }

        if ($ready) {
            Write-Host "Backend is healthy and listening on $BackendUrl." -ForegroundColor Green
        } else {
            Write-Host "Timed out waiting for backend to start. Check $BackendLog" -ForegroundColor Red
            exit 1
        }
    }

    # 3. Check or launch Gram Local Agent
    Write-Host "Checking Gram Local Agent at $LocalAgentUrl..."
    $agentHealthy = $false
    try {
        $res = Invoke-WebRequest -Uri "$LocalAgentUrl/local/v1/health" -Method Get -TimeoutSec 1 -UseBasicParsing -ErrorAction Stop
        if ($res.StatusCode -eq 200) { $agentHealthy = $true }
    } catch {
        $agentHealthy = $false
    }

    if ($agentHealthy) {
        Write-Host "Running." -ForegroundColor Green
    } else {
        Write-Host "Not running. Starting Local Agent in background..." -ForegroundColor Yellow
        $AgentLog = Join-Path $LogsDir "local-agent.log"
        $AgentErrLog = Join-Path $LogsDir "local-agent.err.log"
        $LocalAgentProcess = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--project", "$RootDir\Gram.Blazor\clients\Gram.LocalAgent\Gram.LocalAgent.csproj") -WorkingDirectory $RootDir -RedirectStandardOutput $AgentLog -RedirectStandardError $AgentErrLog -NoNewWindow -PassThru
        Write-Host "Local Agent launched (PID: $($LocalAgentProcess.Id)). Logging to $AgentLog"
        Start-Sleep -Seconds 1
    }

    # 4. Handle Arguments (Headless CLI vs GUI Mode)
    Set-Location $RootDir

    $env:GRAM_BACKEND_URL = $BackendUrl

    if ($Cli -or -not [string]::IsNullOrWhiteSpace($Message)) {
        $msg = if ([string]::IsNullOrWhiteSpace($Message)) { "Hello Gram" } else { $Message }
        Write-Host "Launching in Headless CLI Mode: `"$msg`"" -ForegroundColor Cyan
        dotnet run --project "$RootDir\Gram.Blazor\clients\Gram.Desktop\Gram.Desktop.csproj" -- --backend "$BackendUrl" --message "$msg"
        exit $LASTEXITCODE
    }

    # 5. Native GUI Launch (.NET MAUI Blazor WinUI 3)
    Write-Host "Launching Gram Desktop Native GUI Window (MAUI Blazor)..." -ForegroundColor Cyan
    Write-Host "Press Ctrl+C in this terminal to exit.`n" -ForegroundColor Yellow

    # Refresh the MAUI output before launching it so changes to Main.razor,
    # wwwroot, or the shared Blazor project cannot be hidden by a stale exe.
    dotnet build "$RootDir\Gram.Blazor\clients\Gram.Maui\Gram.Maui.csproj" -f net9.0-windows10.0.19041.0 --no-restore -v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "MAUI Windows build failed (exit code $LASTEXITCODE)."
    }

    $ExePath = Join-Path $RootDir "Gram.Blazor\clients\Gram.Maui\bin\Debug\net9.0-windows10.0.19041.0\win10-x64\Gram.Maui.exe"
    if (Test-Path $ExePath) {
        $guiProc = Start-Process -FilePath $ExePath -PassThru
        $guiProc | Wait-Process
    } else {
        dotnet run --project "$RootDir\Gram.Blazor\clients\Gram.Maui\Gram.Maui.csproj" -f net9.0-windows10.0.19041.0
    }
}
finally {
    Cleanup
}

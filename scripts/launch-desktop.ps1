# ==============================================================================
# Gram Desktop Client Launcher (Windows PowerShell)
# Starts backend server (if not already running) and launches Gram.Desktop
# ==============================================================================

param (
    [string]$BackendUrl = "http://localhost:7337",
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
    Write-Host -NoNewline "Checking Gram backend at $BackendUrl... "
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
        $backendStartInfo = New-Object System.Diagnostics.ProcessStartInfo
        $backendStartInfo.FileName = "dotnet"
        $backendStartInfo.Arguments = "run --project `"$RootDir\Gram.Blazor\Gram.Blazor.csproj`""
        $backendStartInfo.WorkingDirectory = $RootDir
        $backendStartInfo.RedirectStandardOutput = $true
        $backendStartInfo.RedirectStandardError = $true
        $backendStartInfo.UseShellExecute = $false
        $backendStartInfo.CreateNoWindow = $true

        $BackendProcess = [System.Diagnostics.Process]::Start($backendStartInfo)
        Write-Host "Backend process launched (PID: $($BackendProcess.Id)). Logging to $BackendLog"

        $retries = 30
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
    Write-Host -NoNewline "Checking Gram Local Agent at $LocalAgentUrl... "
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
        $agentStartInfo = New-Object System.Diagnostics.ProcessStartInfo
        $agentStartInfo.FileName = "dotnet"
        $agentStartInfo.Arguments = "run --project `"$RootDir\Gram.Blazor\clients\Gram.LocalAgent\Gram.LocalAgent.csproj`""
        $agentStartInfo.WorkingDirectory = $RootDir
        $agentStartInfo.RedirectStandardOutput = $true
        $agentStartInfo.RedirectStandardError = $true
        $agentStartInfo.UseShellExecute = $false
        $agentStartInfo.CreateNoWindow = $true

        $LocalAgentProcess = [System.Diagnostics.Process]::Start($agentStartInfo)
        Write-Host "Local Agent launched (PID: $($LocalAgentProcess.Id)). Logging to $AgentLog"
        Start-Sleep -Seconds 1
    }

    # 4. Handle Arguments (Headless CLI vs GUI Mode)
    Set-Location $RootDir

    if ($Cli -or -not [string]::IsNullOrWhiteSpace($Message)) {
        $msg = if ([string]::IsNullOrWhiteSpace($Message)) { "Hello Gram" } else { $Message }
        Write-Host "Launching in Headless CLI Mode: `"$msg`"" -ForegroundColor Cyan
        dotnet run --project "$RootDir\Gram.Blazor\clients\Gram.Desktop\Gram.Desktop.csproj" -- --backend "$BackendUrl" --message "$msg"
        exit $LASTEXITCODE
    }

    # 5. Native GUI Launch
    Write-Host "Launching Gram Desktop Native GUI Window..." -ForegroundColor Cyan
    Write-Host "Press Ctrl+C in this terminal to exit.`n" -ForegroundColor Yellow

    dotnet run --project "$RootDir\Gram.Blazor\clients\Gram.Desktop\Gram.Desktop.csproj" -- --backend "$BackendUrl"
}
finally {
    Cleanup
}

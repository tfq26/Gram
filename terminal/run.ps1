param(
    [string]$Model = ""
)

if ($Model) {
    $env:GRAM_MODEL = $Model
}

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

Write-Host "Starting Gram Terminal Voice Assistant..." -ForegroundColor Cyan
if (Get-Command py -ErrorAction SilentlyContinue) {
    py -m gram
} else {
    python -m gram
}


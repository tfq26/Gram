param(
    [string]$Model = ""
)

if ($Model) {
    $env:GRAM_MODEL = $Model
}

Write-Host "Starting Gram..." -ForegroundColor Cyan
py -m gram

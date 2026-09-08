[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $env:COPILOT_CLI_BINARY_PATH) {
    $copilotCommand = Get-Command copilot -All -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and (Get-Item $_.Path).Length -gt 0 } |
        Select-Object -First 1
    if ($copilotCommand) {
        $env:COPILOT_CLI_BINARY_PATH = $copilotCommand.Path
    }
}

& dotnet build (Join-Path $root 'VisualSidekick.sln') --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

Write-Host "Visual Sidekick build passed." -ForegroundColor Green

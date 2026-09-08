[CmdletBinding()]
param(
    [switch]$RemoveBuildOutput
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$copilotCommand = Get-Command copilot -All -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and (Get-Item $_.Path).Length -gt 0 } |
    Select-Object -First 1
if (-not $copilotCommand) {
    throw 'GitHub Copilot CLI must be installed and available on PATH.'
}

& $copilotCommand.Path plugin uninstall visual-sidekick
if ($LASTEXITCODE -ne 0) {
    throw "Uninstalling Visual Sidekick failed with exit code $LASTEXITCODE."
}

if ($RemoveBuildOutput) {
    Remove-Item (Join-Path $root '.local') -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Visual Sidekick uninstalled." -ForegroundColor Green

[CmdletBinding()]
param(
    [string]$Model
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'server\VisualSidekick.Server.csproj'
$dist = Join-Path $root '.local\dist'

if (-not $IsWindows) {
    throw 'Visual Sidekick requires Windows.'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET 9 SDK is required. Install it, then run this script again.'
}

$copilotCommand = Get-Command copilot -All -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and (Get-Item $_.Path).Length -gt 0 } |
    Select-Object -First 1
if (-not $copilotCommand) {
    throw 'GitHub Copilot CLI must be installed and available on PATH.'
}

$env:COPILOT_CLI_BINARY_PATH = $copilotCommand.Path

if ($Model) {
    [Environment]::SetEnvironmentVariable('VISUAL_SIDEKICK_MODEL', $Model, 'User')
    Write-Host "Saved VISUAL_SIDEKICK_MODEL for your user account."
}

$runtime = switch ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
    'Arm64' { 'win-arm64' }
    'X64' { 'win-x64' }
    default { throw "Unsupported Windows architecture: $($_)" }
}

if (Test-Path $dist) {
    Remove-Item $dist -Recurse -Force
}

& dotnet publish $project `
    --configuration Release `
    --runtime $runtime `
    --self-contained false `
    -p:PublishSingleFile=true `
    --output $dist
if ($LASTEXITCODE -ne 0) {
    throw "Publishing Visual Sidekick failed with exit code $LASTEXITCODE."
}

& $copilotCommand.Path plugin install $root
if ($LASTEXITCODE -ne 0) {
    throw "Installing the Copilot plugin failed with exit code $LASTEXITCODE."
}

Write-Host "Visual Sidekick installed." -ForegroundColor Green
Write-Host "Restart an existing Copilot CLI session, then run /sidekick-start."

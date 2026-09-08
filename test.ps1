[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $root 'VisualSidekick.sln'
$project = Join-Path $root 'server\VisualSidekick.Server.csproj'
$publish = Join-Path $root '.test-output\publish'
$exe = Join-Path $publish 'visual-sidekick-server.exe'

if (-not $env:COPILOT_CLI_BINARY_PATH) {
    $copilotCommand = Get-Command copilot -All -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and (Get-Item $_.Path).Length -gt 0 } |
        Select-Object -First 1
    if ($copilotCommand) {
        $env:COPILOT_CLI_BINARY_PATH = $copilotCommand.Path
    }
}

& (Join-Path $root 'scripts\Test-PublicSafety.ps1')

& dotnet test $solution --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed with exit code $LASTEXITCODE."
}

if (Test-Path $publish) {
    Remove-Item $publish -Recurse -Force
}

$runtime = switch ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
    'Arm64' { 'win-arm64' }
    'X64' { 'win-x64' }
    default { throw "Unsupported Windows architecture: $($_)" }
}

& dotnet publish $project `
    --configuration Release `
    --runtime $runtime `
    --self-contained false `
    -p:PublishSingleFile=true `
    --output $publish
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) {
    throw 'Smoke-test publish failed.'
}

$windows = @(& $exe --list-windows)
if ($LASTEXITCODE -ne 0) {
    throw 'Window enumeration command failed.'
}

$startInfo = [Diagnostics.ProcessStartInfo]::new($exe)
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.CreateNoWindow = $true

$process = [Diagnostics.Process]::new()
$process.StartInfo = $startInfo
$null = $process.Start()

function Send-McpMessage {
    param([Parameter(Mandatory)][hashtable]$Message)

    $process.StandardInput.WriteLine(($Message | ConvertTo-Json -Compress -Depth 20))
    $process.StandardInput.Flush()
}

Send-McpMessage @{
    jsonrpc = '2.0'
    id = 1
    method = 'initialize'
    params = @{
        protocolVersion = '2025-11-25'
        capabilities = @{}
        clientInfo = @{ name = 'visual-sidekick-smoke-test'; version = '1.0' }
    }
}
$initialize = $process.StandardOutput.ReadLine() | ConvertFrom-Json

Send-McpMessage @{ jsonrpc = '2.0'; method = 'notifications/initialized' }
Send-McpMessage @{ jsonrpc = '2.0'; id = 2; method = 'tools/list'; params = @{} }
$tools = $process.StandardOutput.ReadLine() | ConvertFrom-Json

Send-McpMessage @{
    jsonrpc = '2.0'
    id = 3
    method = 'tools/call'
    params = @{ name = 'status'; arguments = @{} }
}
$status = $process.StandardOutput.ReadLine() | ConvertFrom-Json

$process.StandardInput.Close()
if (-not $process.WaitForExit(5000)) {
    $process.Kill($true)
}

$requiredTools = @(
    'start',
    'start_live',
    'select_region',
    'capture_current',
    'capture_changes',
    'status',
    'pause',
    'resume',
    'set_interval',
    'stop'
)
$toolNames = @($tools.result.tools.name)
$missingTools = @($requiredTools | Where-Object { $_ -notin $toolNames })

if ($missingTools.Count -gt 0) {
    throw "Missing MCP tools: $($missingTools -join ', ')"
}

if ($initialize.result.protocolVersion -ne '2025-11-25') {
    throw "Unexpected MCP protocol version: $($initialize.result.protocolVersion)"
}

if ($status.result.isError) {
    throw 'Status tool returned an error.'
}

Write-Host "Visual Sidekick tests passed." -ForegroundColor Green
Write-Host "Observable windows in this session: $($windows.Count)"
Write-Host "Registered MCP tools: $($toolNames.Count)"

[CmdletBinding()]
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path $Root).Path
$scannerRelativePath = 'scripts/Test-PublicSafety.ps1'
$binaryExtensions = @('.exe', '.dll', '.pdb', '.zip', '.nupkg', '.msi', '.so', '.dylib', '.node')
$privateConfigNames = @('.env', '.env.local', '.env.development', '.env.production', 'secrets.json', 'appsettings.local.json')
$failures = [Collections.Generic.List[string]]::new()

$tracked = @(& git -C $rootPath ls-files 2>$null)
if ($LASTEXITCODE -ne 0 -or $tracked.Count -eq 0) {
    $tracked = @(
        Get-ChildItem $rootPath -Recurse -File |
            Where-Object {
                $_.FullName -notmatch '[\\/](?:\.git|bin|obj|\.local|\.test-output)[\\/]'
            } |
            ForEach-Object {
                [IO.Path]::GetRelativePath($rootPath, $_.FullName).Replace('\', '/')
            }
    )
}

$configuredEmail = (@(& git -C $rootPath config user.email 2>$null) -join '').Trim()
if ($configuredEmail -match '(?i)@microsoft\.com$') {
    $failures.Add('Git author email uses a work address. Configure a public noreply address locally.')
}

$commitEmails = @(& git -C $rootPath log -1 --format='%ae%n%ce' 2>$null)
foreach ($commitEmail in $commitEmails) {
    if ($commitEmail -match '(?i)@microsoft\.com$') {
        $failures.Add('Latest commit metadata contains a work email address.')
    }
}

foreach ($relativePath in $tracked) {
    $extension = [IO.Path]::GetExtension($relativePath).ToLowerInvariant()
    if ($extension -in $binaryExtensions) {
        $failures.Add("Tracked binary or package: $relativePath")
    }

    $normalizedPath = $relativePath.Replace('\', '/')
    if ($normalizedPath -match '(?i)(^|/)(?:bin|obj|\.local|\.test-output)/') {
        $failures.Add("Tracked build output: $relativePath")
    }

    if ([IO.Path]::GetFileName($relativePath).ToLowerInvariant() -in $privateConfigNames) {
        $failures.Add("Tracked private configuration: $relativePath")
    }
}

$patterns = @(
    @{
        Name = 'personal Windows home path'
        Regex = '(?i)[a-z]:[\\/]+users[\\/]+(?!public(?:[\\/]|$)|default(?: user)?(?:[\\/]|$)|all users(?:[\\/]|$))[^\\/\s]+'
    },
    @{
        Name = 'work email address'
        Regex = '(?i)\b[a-z0-9._%+-]+@microsoft\.com\b'
    },
    @{
        Name = 'managed tenant address'
        Regex = '(?i)\b[a-z0-9._%+-]+@[a-z0-9.-]+\.onmicrosoft\.com\b'
    },
    @{
        Name = 'tenant identifier'
        Regex = '(?i)\btenant(?:id| id)?\s*[:=]\s*["'']?[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}'
    },
    @{
        Name = 'credential or access token'
        Regex = '(?i)\b(?:ghp_|gho_|github_pat_|sk-[a-z0-9]|bearer\s+[a-z0-9._-]{20,}|client_secret\s*[:=])'
    },
    @{
        Name = 'unsafe positioning'
        Regex = '(?i)\b(?:evade|bypass)\s+(?:a\s+)?proctor|\bspy\s+on\b|\bcovert(?:ly)?\s+(?:monitor|record|observe)|\bcredential\s+capture\b|\bemployee\s+monitoring\b|\bcheat(?:ing)?\s+(?:on|during|at)\b'
    }
)

foreach ($relativePath in $tracked) {
    if ($relativePath.Replace('\', '/') -eq $scannerRelativePath) {
        continue
    }

    $path = Join-Path $rootPath $relativePath
    if (-not (Test-Path $path -PathType Leaf)) {
        continue
    }

    $content = Get-Content $path -Raw -ErrorAction Stop
    foreach ($pattern in $patterns) {
        if ($content -match $pattern.Regex) {
            $failures.Add("$($pattern.Name): $relativePath")
        }
    }
}

if ($failures.Count -gt 0) {
    $failures | Sort-Object -Unique | ForEach-Object { Write-Error $_ }
    throw "Public safety scan failed with $($failures.Count) finding(s)."
}

Write-Host "Public safety scan passed for $($tracked.Count) files." -ForegroundColor Green

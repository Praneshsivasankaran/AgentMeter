#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Directory)

$ErrorActionPreference = 'Stop'
function Assert-RuntimeNotices {
    param([string]$Directory)
    if (-not (Test-Path -LiteralPath (Join-Path $Directory 'coreclr.dll') -PathType Leaf)) { return }
    $runtimeConfig = Get-Content -LiteralPath (Join-Path $Directory 'AgentMeter.runtimeconfig.json') -Raw | ConvertFrom-Json
    $frameworks = @($runtimeConfig.runtimeOptions.includedFrameworks)
    if ($frameworks.Count -eq 0 -or 'Microsoft.NETCore.App' -notin $frameworks.name) {
        throw 'Bundled runtime framework metadata is missing.'
    }
    $required = @('Microsoft.NETCore.App-THIRD-PARTY-NOTICES.txt')
    foreach ($framework in $frameworks) {
        if ($framework.name -notmatch '^Microsoft\.[A-Za-z0-9.]+$') { throw 'Unexpected bundled framework name.' }
        $required += "$($framework.name)-LICENSE.txt"
    }
    foreach ($notice in $required) {
        $path = Join-Path (Join-Path $Directory 'licenses') $notice
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
            throw "Missing bundled runtime notice: $notice"
        }
    }
}

Assert-RuntimeNotices -Directory $Directory
$repository = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$buildUserProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
$forbiddenPaths = @($repository, $buildUserProfile) | Where-Object { $_.Length -gt 0 }
if (@(Get-ChildItem -LiteralPath $Directory -Filter '*.pdb' -File -Recurse).Count -ne 0) {
    throw 'A release unexpectedly contains debug symbols.'
}
$binaries = @(Get-ChildItem -LiteralPath $Directory -File | Where-Object {
    $_.Name -like 'AgentMeter*' -and $_.Extension -in @('.dll', '.exe')
})
if ($binaries.Count -lt 3) { throw 'Expected AgentMeter release binaries were not found.' }
foreach ($binary in $binaries) {
    $bytes = [IO.File]::ReadAllBytes($binary.FullName)
    foreach ($encoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode, [Text.Encoding]::BigEndianUnicode)) {
        $offsets = if ($encoding.CodePage -eq 65001) { @(0) } else { @(0, 1) }
        foreach ($offset in $offsets) {
            if ($bytes.Length -le $offset) { continue }
            $text = $encoding.GetString($bytes, $offset, $bytes.Length - $offset)
            foreach ($path in $forbiddenPaths) {
                foreach ($candidate in @($path, $path.Replace('\', '/'))) {
                    if ($text.IndexOf($candidate, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                        throw "Release binary contains a private build path: $($binary.Name)"
                    }
                }
            }
            # The SDK's native apphost carries Microsoft's anonymous CI symbol path.
            # Our managed assemblies must carry no absolute debug-symbol paths.
            if ($binary.Extension -eq '.dll' -and $text -match '(?i)[a-z]:[\\/][^\x00\r\n]{0,512}\.pdb\b') {
                throw "Release binary contains an absolute debug-symbol path: $($binary.Name)"
            }
        }
    }
}
$forbidden = @(Get-ChildItem -LiteralPath $Directory -Recurse -Force | Where-Object {
    $_.Name -match '(?i)^(ClaudeBridge|node\.exe|sdk\.mjs|claude\.exe|codex\.exe|node_modules)$' -or $_.Name -match '(?i)anthropic|claude-agent-sdk'
})
if ($forbidden.Count -gt 0) { throw 'V2 must not redistribute provider executables, Node, or the Claude SDK.' }
Write-Host "Release metadata and V2 dependency guard passed for $($binaries.Count) AgentMeter binaries."

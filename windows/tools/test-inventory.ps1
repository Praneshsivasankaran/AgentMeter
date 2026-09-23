#requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $PackageDirectory).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ('AgentMeter-InventoryTests-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $root
foreach ($file in @('Llumi.dll','Llumi.runtimeconfig.json','LICENSE.txt','THIRD-PARTY-NOTICES.txt')) { Copy-Item -LiteralPath (Join-Path $source $file) -Destination $root }
foreach ($projection in @('Microsoft.Windows.SDK.NET.dll','WinRT.Runtime.dll')) { if (Test-Path (Join-Path $source $projection)) { Copy-Item -LiteralPath (Join-Path $source $projection) -Destination $root } }
if (Test-Path -LiteralPath (Join-Path $source 'licenses')) { Copy-Item -LiteralPath (Join-Path $source 'licenses') -Destination $root -Recurse }
& "$PSScriptRoot/write-inventory.ps1" -Directory $root
$inventory = Get-Content -LiteralPath (Join-Path $root 'dependency-inventory.json') -Raw | ConvertFrom-Json
if ($inventory.schemaVersion -ne 2) { throw 'Wrong schema' }
if (@($inventory.components | Where-Object { $_.bundled -and -not $_.noticePresent }).Count) { throw 'Missing notices' }
if (@($inventory.components | Where-Object { $_.component -in @('Codex CLI','Claude Code','Claude Agent SDK','Node.js') -and $_.bundled }).Count) { throw 'Provider payload claimed bundled' }
foreach ($file in $inventory.files) { if ((Get-FileHash -LiteralPath (Join-Path $root $file.path)).Hash -ne $file.sha256) { throw 'Inventory digest mismatch' } }
# A missing MIT notice must fail inventory generation, not merely record success.
Remove-Item -LiteralPath (Join-Path $root 'LICENSE.txt')
$failed = $false
try { & "$PSScriptRoot/write-inventory.ps1" -Directory $root }
catch { if ($_.Exception.Message -ne 'A redistributed component is missing its required notice.') { throw }; $failed = $true }
if (-not $failed) { throw 'Missing MIT notice was accepted' }
Copy-Item -LiteralPath (Join-Path $source 'LICENSE.txt') -Destination $root
$checks = 5
foreach ($notice in @('Microsoft.Windows.SDK-LICENSE.rtf','CsWinRT-LICENSE.txt')) {
    $file = Join-Path $root "licenses/$notice"
    if (-not (Test-Path $file)) { continue }
    Remove-Item -LiteralPath $file
    $failed = $false
    try { & "$PSScriptRoot/write-inventory.ps1" -Directory $root } catch { if ($_.Exception.Message -ne 'A redistributed component is missing its required notice.') { throw }; $failed = $true }
    if (-not $failed) { throw "Missing projection notice accepted: $notice" }
    Copy-Item -LiteralPath (Join-Path $source "licenses/$notice") -Destination $file
    $checks++
}
Write-Host "Inventory checks: $checks passed."

#requires -Version 5.1
$ErrorActionPreference = 'Stop'

# Load only the functions under test: do not invoke publishing or dependency downloads.
foreach ($entry in @(@('runtime-notices.ps1', 'Copy-RuntimeNotices'), @('assert-release.ps1', 'Assert-RuntimeNotices'))) {
    $tokens = $null
    $parseErrors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $entry[0]), [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -ne 0) { throw "PowerShell parse error in $($entry[0])." }
    $function = $ast.Find({ param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $entry[1]
    }, $false)
    if ($null -eq $function) { throw "Missing function $($entry[1])." }
    . ([scriptblock]::Create($function.Extent.Text))
}

$temporaryRoot = [IO.Path]::GetTempPath()
$testRoot = Join-Path $temporaryRoot ('AgentMeter-NoticeTests-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot
$passed = 0
function Require-Failure {
    param([scriptblock]$Action)
    $failed = $false
    try { & $Action } catch { $failed = $true }
    if (-not $failed) { throw 'Expected validation failure did not occur.' }
}
try {
    $corePack = Join-Path $testRoot 'alternate-cache\core\123.4.5'
    $desktopPack = Join-Path $testRoot 'another-cache\desktop\123.4.5'
    $destination = Join-Path $testRoot 'release'
    $null = New-Item -ItemType Directory -Path $corePack, $desktopPack, $destination -Force
    [IO.File]::WriteAllText((Join-Path $corePack 'LICENSE.TXT'), 'Core license fixture')
    [IO.File]::WriteAllText((Join-Path $corePack 'THIRD-PARTY-NOTICES.TXT'), 'Core notice fixture')
    [IO.File]::WriteAllText((Join-Path $desktopPack 'LICENSE'), 'Desktop license fixture')
    [IO.File]::WriteAllText((Join-Path $desktopPack 'ThirdPartyNotices.txt'), 'Desktop notice fixture')
    $frameworks = @(
        [pscustomobject]@{ name = 'Microsoft.NETCore.App'; version = '123.4.5' },
        [pscustomobject]@{ name = 'Microsoft.WindowsDesktop.App'; version = '123.4.5' }
    )
    $packs = @(
        [pscustomobject]@{ FrameworkName = $frameworks[0].name; NuGetPackageVersion = '123.4.5'; PackageDirectory = $corePack },
        [pscustomobject]@{ FrameworkName = $frameworks[1].name; NuGetPackageVersion = '123.4.5'; PackageDirectory = $desktopPack }
    )
    Copy-RuntimeNotices -RuntimePacks $packs -Frameworks $frameworks -Destination $destination
    foreach ($pair in @(
        @((Join-Path $corePack 'LICENSE.TXT'), 'Microsoft.NETCore.App-LICENSE.txt'),
        @((Join-Path $corePack 'THIRD-PARTY-NOTICES.TXT'), 'Microsoft.NETCore.App-THIRD-PARTY-NOTICES.txt'),
        @((Join-Path $desktopPack 'LICENSE'), 'Microsoft.WindowsDesktop.App-LICENSE.txt'),
        @((Join-Path $desktopPack 'ThirdPartyNotices.txt'), 'Microsoft.WindowsDesktop.App-THIRD-PARTY-NOTICES.txt')
    )) {
        if ((Get-FileHash -LiteralPath $pair[0]).Hash -ne (Get-FileHash -LiteralPath (Join-Path $destination ('licenses\' + $pair[1]))).Hash) {
            throw 'Copied vendor notice was altered.'
        }
    }
    $passed++
    [IO.File]::WriteAllText((Join-Path $destination 'coreclr.dll'), 'Runtime marker fixture')
    @{ runtimeOptions = @{ includedFrameworks = $frameworks } } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $destination 'AgentMeter.runtimeconfig.json')
    Assert-RuntimeNotices -Directory $destination
    $passed++

    $packs[0].NuGetPackageVersion = '999.0.0'
    Require-Failure { Copy-RuntimeNotices -RuntimePacks $packs -Frameworks $frameworks -Destination $destination }
    $packs[0].NuGetPackageVersion = '123.4.5'
    $passed++
    Require-Failure { Copy-RuntimeNotices -RuntimePacks ($packs + $packs[0]) -Frameworks $frameworks -Destination $destination }
    $passed++
    Require-Failure { Copy-RuntimeNotices -RuntimePacks $packs -Frameworks @() -Destination $destination }
    $passed++
    [IO.File]::WriteAllText((Join-Path $corePack 'THIRD-PARTY-NOTICES.TXT'), '')
    Require-Failure { Copy-RuntimeNotices -RuntimePacks $packs -Frameworks $frameworks -Destination $destination }
    $passed++
    [IO.File]::WriteAllText((Join-Path $corePack 'THIRD-PARTY-NOTICES.TXT'), 'Core notice fixture')
    Remove-Item -LiteralPath (Join-Path $desktopPack 'ThirdPartyNotices.txt')
    Copy-RuntimeNotices -RuntimePacks $packs -Frameworks $frameworks -Destination $destination
    $passed++
    Remove-Item -LiteralPath (Join-Path $desktopPack 'LICENSE')
    Require-Failure { Copy-RuntimeNotices -RuntimePacks $packs -Frameworks $frameworks -Destination $destination }
    $passed++
    [IO.File]::WriteAllText((Join-Path $destination 'licenses\Microsoft.WindowsDesktop.App-LICENSE.txt'), '')
    Require-Failure { Assert-RuntimeNotices -Directory $destination }
    $passed++
    $frameworkDependent = Join-Path $testRoot 'framework-dependent'
    $null = New-Item -ItemType Directory -Path $frameworkDependent
    Assert-RuntimeNotices -Directory $frameworkDependent
    $passed++
    Write-Host "Runtime notice tests: $passed passed, 0 failed."
}
finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if ([IO.Path]::GetDirectoryName($resolved).TrimEnd('\') -ne [IO.Path]::GetFullPath($temporaryRoot).TrimEnd('\') -or
        [IO.Path]::GetFileName($resolved) -notmatch '^AgentMeter-NoticeTests-[a-f0-9]{32}$') {
        throw 'Refusing to clean outside the generated notice test directory.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

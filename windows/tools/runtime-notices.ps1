function Copy-RuntimeNotices {
    param([object[]]$RuntimePacks, [object[]]$Frameworks, [string]$Destination)
    if ($Frameworks.Count -eq 0) { throw 'The self-contained release did not identify its bundled frameworks.' }
    $noticeDirectory = Join-Path $Destination 'licenses'
    $null = New-Item -ItemType Directory -Path $noticeDirectory -Force
    foreach ($framework in $Frameworks) {
        if ($framework.name -notmatch '^Microsoft\.[A-Za-z0-9.]+$') { throw 'Unexpected bundled framework name.' }
        # Resolve the exact published version, not the newest SDK/cache directory on this machine.
        $matchingPacks = @($RuntimePacks | Where-Object {
            $_.FrameworkName -eq $framework.name -and $_.NuGetPackageVersion -eq $framework.version
        })
        if ($matchingPacks.Count -ne 1 -or -not (Test-Path -LiteralPath $matchingPacks[0].PackageDirectory -PathType Container)) {
            throw "Cannot locate the resolved runtime pack for $($framework.name)."
        }
        $pack = $matchingPacks[0].PackageDirectory
        $license = @(Get-ChildItem -LiteralPath $pack -File | Where-Object { $_.Name -match '^LICENSE(?:\.txt|\.md)?$' })
        if ($license.Count -ne 1 -or $license[0].Length -eq 0) {
            throw "Missing or ambiguous license in the resolved runtime pack for $($framework.name)."
        }
        Copy-Item -LiteralPath $license[0].FullName -Destination (Join-Path $noticeDirectory "$($framework.name)-LICENSE.txt")
        $notices = @(Get-ChildItem -LiteralPath $pack -File | Where-Object { $_.Name -match '^THIRD[-_]?PARTY[-_]?NOTICES?(?:\.txt|\.md)?$' })
        if ($notices.Count -gt 1 -or ($notices.Count -eq 1 -and $notices[0].Length -eq 0) -or
            ($framework.name -eq 'Microsoft.NETCore.App' -and $notices.Count -eq 0)) {
            throw "Missing or ambiguous third-party notices in the resolved runtime pack for $($framework.name)."
        }
        if ($notices.Count -eq 1) {
            Copy-Item -LiteralPath $notices[0].FullName -Destination (Join-Path $noticeDirectory "$($framework.name)-THIRD-PARTY-NOTICES.txt")
        }
    }
}

param([Parameter(Mandatory = $true)][string]$FixtureDirectory)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testDirectory = Join-Path $repositoryRoot ('obj\publish-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$candidate = 'b' * 40
$otherCommit = 'a' * 40

# This function shadows the native CLI. These tests never access GitHub or credentials.
function gh {
    $arguments = @($args)
    $state.Calls.Add(($arguments -join ' '))
    $global:LASTEXITCODE = 0
    if ($arguments[0] -eq 'api') {
        $state.Reads++
        if ($state.Mode -eq 'api-failure') { $global:LASTEXITCODE = 1; return }
        if ($state.Mode -eq 'stale' -or ($state.Mode -eq 'advanced' -and $state.Reads -eq 2)) { return $otherCommit }
        return $candidate
    }
    if ($arguments[0] -eq 'release' -and $arguments[1] -eq 'create') {
        if ($arguments -notcontains '--draft' -or $arguments -notcontains '--latest=false') { throw 'An incomplete release could become Latest.' }
        if ($state.Mode -eq 'upload-failure') { $global:LASTEXITCODE = 1 }
        return
    }
    if ($arguments[0] -eq 'release' -and $arguments[1] -eq 'edit') {
        if ($state.Reads -ne 2 -or $arguments -notcontains '--draft=false' -or $arguments -notcontains '--latest') { throw 'Publishing without final verification.' }
        if ($state.Mode -eq 'publish-failure') { $global:LASTEXITCODE = 1 }
        return
    }
    throw 'Unexpected GitHub CLI operation.'
}

try {
    foreach ($mode in @('success', 'stale', 'advanced', 'api-failure', 'upload-failure', 'publish-failure', 'tampered')) {
        $state = [pscustomobject]@{ Mode = $mode; Reads = 0; Calls = [Collections.Generic.List[string]]::new() }
        $summary = Join-Path $testDirectory "$mode-summary.txt"
        $inputPath = $FixtureDirectory
        if ($mode -eq 'tampered') {
            $inputPath = Join-Path $testDirectory 'tampered'
            New-Item -ItemType Directory -Path $inputPath | Out-Null
            Copy-Item -LiteralPath (Join-Path $FixtureDirectory 'ScreenClip-windows-x64.zip') -Destination $inputPath
            $badManifest = Get-Content -LiteralPath (Join-Path $FixtureDirectory 'update.json') -Raw | ConvertFrom-Json
            $badManifest.sha256 = '0' * 64
            $badManifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $inputPath 'update.json') -Encoding utf8
        }
        $failed = $false
        try {
            & (Join-Path $repositoryRoot 'publish.ps1') -PackageDirectory $inputPath -Repository 'crono2250/ScreenClip' `
                -Commit $candidate -RunId '123' -Attempt '1' -TemporaryDirectory $testDirectory -SummaryPath $summary
        }
        catch { $failed = $true }
        $expectedFailure = $mode -in @('api-failure', 'upload-failure', 'publish-failure', 'tampered')
        if ($failed -ne $expectedFailure) { throw "Unexpected release outcome: $mode" }
        $published = @($state.Calls | Where-Object { $_ -like 'release edit *' }).Count
        if ($published -ne [int]($mode -in @('success', 'publish-failure'))) { throw "Unexpected publication attempt: $mode" }
        if ((Test-Path -LiteralPath $summary) -ne ($mode -eq 'success')) { throw "Publication reported too early: $mode" }
        if ($mode -in @('stale', 'api-failure', 'tampered') -and @($state.Calls | Where-Object { $_ -like 'release create *' }).Count -ne 0) {
            throw "Invalid build uploaded: $mode"
        }
    }
    Write-Host 'OK: release publication, stale builds, main advancement, metadata tampering and upload/publish failures'
}
finally {
    # The directory is created above under obj with a fresh, fixed prefix.
    $resolvedTestDirectory = [IO.Path]::GetFullPath($testDirectory)
    $expectedParent = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'obj'))
    if ([IO.Path]::GetDirectoryName($resolvedTestDirectory) -eq $expectedParent -and
        [IO.Path]::GetFileName($resolvedTestDirectory) -like 'publish-tests-*') {
        Remove-Item -LiteralPath $resolvedTestDirectory -Recurse -Force
    }
}

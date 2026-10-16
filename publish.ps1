param(
    [string]$PackageDirectory = (Join-Path $PSScriptRoot 'dist'),
    [string]$Repository = $env:GITHUB_REPOSITORY,
    [string]$Commit = $env:GITHUB_SHA,
    [string]$RunId = $env:GITHUB_RUN_ID,
    [string]$Attempt = $env:GITHUB_RUN_ATTEMPT,
    [string]$TemporaryDirectory = $env:RUNNER_TEMP,
    [string]$SummaryPath = $env:GITHUB_STEP_SUMMARY
)

$ErrorActionPreference = 'Stop'
if ($Repository -cne 'crono2250/ScreenClip' -or $Commit -cnotmatch '^[0-9a-f]{40}$' -or
    $RunId -notmatch '^[0-9]+$' -or $Attempt -notmatch '^[0-9]+$') {
    throw 'Invalid release context.'
}
$head = & gh api "repos/$Repository/git/ref/heads/main" --jq '.object.sha'
if ($LASTEXITCODE -ne 0) { throw 'Could not verify the main branch.' }
if ($head -cne $Commit) {
    Write-Host 'Skipping an older build because main has advanced.'
    return
}
$manifestPath = Join-Path $PackageDirectory 'update.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$tag = "build-$Commit-$RunId-$Attempt"
$archive = Join-Path $PackageDirectory 'ScreenClip-windows-x64.zip'
$expectedUrl = "https://github.com/$Repository/releases/download/$tag/ScreenClip-windows-x64.zip"
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($manifest.schema_version -ne 1 -or $manifest.repository -cne $Repository -or $manifest.branch -cne 'main' -or
    $manifest.commit -cne $Commit -or $manifest.release_tag -cne $tag -or $manifest.archive_url -cne $expectedUrl -or
    $manifest.sha256 -cne $hash -or $manifest.size -ne (Get-Item -LiteralPath $archive).Length) {
    throw 'Release metadata does not match the tested build.'
}
$notesPath = Join-Path $TemporaryDirectory "screenclip-release-$RunId-$Attempt.txt"
$notes = "Commit: $Commit`n`nTested Windows x64 build. Automatic updates require no user token."
[IO.File]::WriteAllText($notesPath, $notes)
try {
    & gh release create $tag $archive $manifestPath --repo $Repository --target $Commit `
        --title "ScreenClip $($Commit.Substring(0, 12))" --draft --latest=false --notes-file $notesPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not upload the draft release.' }
    # An old workflow rerun must never replace a newer Latest.
    $head = & gh api "repos/$Repository/git/ref/heads/main" --jq '.object.sha'
    if ($LASTEXITCODE -ne 0) { throw 'Could not verify main before publishing.' }
    if ($head -cne $Commit) {
        Write-Host 'Leaving this release as a draft because main has advanced.'
        return
    }
    & gh release edit $tag --repo $Repository --draft=false --latest
    if ($LASTEXITCODE -ne 0) { throw 'Could not publish the release.' }
    Add-Content -LiteralPath $SummaryPath -Value @(
        '### Public release'
        "- [Download ScreenClip](https://github.com/$Repository/releases/tag/$tag)"
        '- Automatic updates need no GitHub login or personal access token.'
    )
}
finally {
    Remove-Item -LiteralPath $notesPath -ErrorAction SilentlyContinue
}

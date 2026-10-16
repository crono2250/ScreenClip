param(
    [string]$InputDirectory = (Join-Path $PSScriptRoot 'dist'),
    [Parameter(Mandatory = $true)][string]$Commit,
    [string]$ReleaseTag = "build-$Commit"
)

$ErrorActionPreference = 'Stop'
if ($Commit -cnotmatch '^[0-9a-f]{40}$' -or $ReleaseTag -cnotmatch "^build-$Commit(-[0-9]+-[0-9]+)?$") {
    throw 'コミット ID またはリリースタグが無効です。'
}
$packageDirectory = (Resolve-Path -LiteralPath $InputDirectory).Path
$exe = Join-Path $packageDirectory 'ScreenClip.exe'
$assembly = [Reflection.Assembly]::LoadFile($exe)
$version = $assembly.GetCustomAttributes([Reflection.AssemblyInformationalVersionAttribute], $false)[0].InformationalVersion
if ($version -cne $Commit -or $assembly.GetName().Name -cne 'ScreenClip') {
    throw '実行ファイルのコミット ID がパッケージのコミットと一致しません。'
}
$peKind = [Reflection.PortableExecutableKinds]0
$machine = [Reflection.ImageFileMachine]::I386
$assembly.ManifestModule.GetPEKind([ref]$peKind, [ref]$machine)
if ($machine -ne [Reflection.ImageFileMachine]::AMD64) { throw 'x64 ビルドではありません。' }

$names = @('ScreenClip.exe', 'ScreenClip.exe.config')
$checksums = foreach ($name in $names) {
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $packageDirectory $name)).Hash.ToLowerInvariant()
    "$hash  $name"
}
$checksums | Set-Content -LiteralPath (Join-Path $packageDirectory 'SHA256SUMS') -Encoding ascii
$payload = @('ScreenClip.exe', 'ScreenClip.exe.config', 'SHA256SUMS') | ForEach-Object { Join-Path $packageDirectory $_ }
$archiveName = 'ScreenClip-windows-x64.zip'
$archive = Join-Path $packageDirectory $archiveName
Compress-Archive -LiteralPath $payload -DestinationPath $archive -CompressionLevel Optimal -Force
$archiveFile = Get-Item -LiteralPath $archive
if ($archiveFile.Length -gt 32MB) { throw '配布 ZIP が自動更新のサイズ上限を超えています。' }
$manifest = [ordered]@{
    schema_version = 1
    repository = 'crono2250/ScreenClip'
    branch = 'main'
    commit = $Commit
    release_tag = $ReleaseTag
    archive_url = "https://github.com/crono2250/ScreenClip/releases/download/$ReleaseTag/$archiveName"
    sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant()
    size = $archiveFile.Length
}
$manifestPath = Join-Path $packageDirectory 'update.json'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Write-Host "配布パッケージ完成: $archiveName, update.json ($ReleaseTag)"

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fixtureDirectory = Join-Path $root 'obj\update-test-fixture'
New-Item -ItemType Directory -Path $fixtureDirectory -Force | Out-Null
& $compiler /nologo /target:exe /platform:x64 "/out:$fixtureDirectory\ScreenClip.exe" (Join-Path $root 'tests\UpdateFixture.cs')
if ($LASTEXITCODE -ne 0) { throw "更新テスト用ビルドに失敗しました: $LASTEXITCODE" }
Copy-Item -LiteralPath (Join-Path $root 'App.config') -Destination (Join-Path $fixtureDirectory 'ScreenClip.exe.config') -Force
& (Join-Path $root 'package.ps1') -InputDirectory $fixtureDirectory -Commit ('b' * 40) -ReleaseTag ('build-' + ('b' * 40) + '-123-1')
$mismatchRejected = $false
try { & (Join-Path $root 'package.ps1') -InputDirectory $fixtureDirectory -Commit ('a' * 40) }
catch { $mismatchRejected = $true }
if (-not $mismatchRejected) { throw '異なるコミットのパッケージ化が拒否されませんでした。' }
& (Join-Path $root 'tests\PublishTests.ps1') -FixtureDirectory $fixtureDirectory
$output = Join-Path $root 'tests\SmokeTests.exe'
$sources = @('Program.cs', 'AppSettings.cs', 'StartupRegistration.cs', 'CaptureOverlay.cs', 'EditorForm.cs', 'SettingsForm.cs', 'HotkeyCaptureBox.cs', 'VersionInfo.cs', 'AutoUpdater.cs', 'UpdateInstaller.cs', 'tests\SmokeTests.cs', 'tests\UpdateTests.cs') |
    ForEach-Object { Join-Path $root $_ }
& $compiler /nologo /target:exe /platform:x64 /codepage:65001 "/out:$output" `
    /main:ScreenClip.SmokeTests /reference:System.dll /reference:System.Core.dll `
    /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    /reference:System.Runtime.Serialization.dll `
    /reference:System.Net.Http.dll /reference:System.IO.Compression.dll $sources
if ($LASTEXITCODE -ne 0) { throw "テストのビルドに失敗しました: $LASTEXITCODE" }
& $output
if ($LASTEXITCODE -ne 0) { throw "テストに失敗しました: $LASTEXITCODE" }

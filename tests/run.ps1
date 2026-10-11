$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $root 'tests\SmokeTests.exe'
$sources = @('Program.cs', 'AppSettings.cs', 'CaptureOverlay.cs', 'EditorForm.cs', 'SettingsForm.cs', 'HotkeyCaptureBox.cs', 'VersionInfo.cs', 'tests\SmokeTests.cs') |
    ForEach-Object { Join-Path $root $_ }
& $compiler /nologo /target:exe /platform:x64 /codepage:65001 "/out:$output" `
    /main:ScreenClip.SmokeTests /reference:System.dll /reference:System.Core.dll `
    /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    /reference:System.Runtime.Serialization.dll $sources
if ($LASTEXITCODE -ne 0) { throw "テストのビルドに失敗しました: $LASTEXITCODE" }
& $output
if ($LASTEXITCODE -ne 0) { throw "テストに失敗しました: $LASTEXITCODE" }

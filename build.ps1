$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw '.NET Framework 4.x の 64bit C# コンパイラが見つかりません。'
}
$output = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$exe = Join-Path $output 'ScreenClip.exe'
$safeRoot = $root.Replace('\', '/')
$commit = (& git -c "safe.directory=$safeRoot" -C $root rev-parse HEAD 2>$null)
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { $commit = 'unversioned' }
$objectDir = Join-Path $root 'obj'
New-Item -ItemType Directory -Path $objectDir -Force | Out-Null
$generated = Join-Path $objectDir 'BuildCommit.cs'
@"
using System.Reflection;
[assembly: AssemblyInformationalVersion("$commit")]
namespace ScreenClip { internal static class BuildCommit { public const string Id = "$commit"; } }
"@ | Set-Content -LiteralPath $generated -Encoding UTF8
$sources = @('Program.cs', 'AppSettings.cs', 'CaptureOverlay.cs', 'EditorForm.cs', 'SettingsForm.cs', 'HotkeyCaptureBox.cs', 'VersionInfo.cs') |
    ForEach-Object { Join-Path $root $_ }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /define:BUILD_COMMIT `
    "/out:$exe" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll $sources $generated
if ($LASTEXITCODE -ne 0) { throw "ビルドに失敗しました: $LASTEXITCODE" }
Copy-Item -LiteralPath (Join-Path $root 'App.config') -Destination (Join-Path $output 'ScreenClip.exe.config') -Force
Write-Host "完成: dist/ScreenClip.exe (commit: $commit)"

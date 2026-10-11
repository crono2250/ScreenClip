# ScreenClip

Windows 用の 64bit 常駐スクリーンショットアプリです。撮影する範囲をマウスで選び、保存前に黒塗りや赤枠を追加できます。

## 起動と操作

1. `build.ps1` で生成した `dist/ScreenClip.exe` を起動します。通知領域にアイコンが表示されます。
2. 初期設定の `Ctrl + Shift + S` を押すか、通知領域のアイコンをダブルクリックします。
3. 暗くなった画面をドラッグして撮影範囲を選びます。選択範囲だけ元の明るさで表示されます。`Esc` でキャンセルできます。
4. 確認画面で「黒塗」または「赤枠」を押し、画像上をドラッグすると加工できます。「元に戻す」または `Ctrl + Z` で直前の加工を取り消せます。
5. 「保存」を押すと選択範囲のみを画像ファイルとして保存します。

ファイル名は `YYYY-MMDD-SS(X x Y).png` の形式です。JPG 設定では拡張子が `.jpg` になります。同じ名前の画像がある場合は末尾に `_1` などを追加します。保存後は確認画面がそのまま閉じます。

通知領域のアイコンを右クリックして「設定」を開くと、キー入力欄でホットキーを押して登録できます。PrintScreen 単体も指定できます。PNG/JPG と保存先フォルダーも変更できます。保存先が空欄なら実行ファイルと同じフォルダーに保存します。設定画面の Version 表示はビルド元の Git コミットを示します。「終了」で常駐を終了します。

## 必要な環境

- Windows 10/11 の 64bit 版
- .NET Framework 4.8 以降

## ソースからビルド

PowerShell で `./build.ps1` を実行します。Windows に付属する .NET Framework の 64bit C# コンパイラを使用し、`dist/ScreenClip.exe` を生成します。Visual Studio では `ScreenClip.csproj` も開けます。

設定は `%APPDATA%\ScreenClip\settings.json` に保存されます。JPG は品質 90 で保存します。

## CI の成果物

`main` への push と手動実行で [Build Windows x64](https://github.com/crono2250/ScreenClip/actions/workflows/windows-build.yml) がテストと x64 ビルドを行います。実行結果の **Artifacts** に `ScreenClip-windows-x64` が表示され、実行ファイル、設定ファイル、SHA-256 チェックサムをダウンロードできます。成果物は 30 日間保持されます。GitHub の Artifacts をダウンロードするにはサインインが必要です。

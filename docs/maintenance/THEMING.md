# UIの配色

`src/LtcPlayer.Avalonia/ThemePalette.cs` 冒頭の「基本色」10個を編集します。ホバー・押下・選択などは自動生成するため、個別に指定する必要はありません。変更後は再ビルドしてください。実行中のテーマ切り替え機能ではありません。

| 基本色 | 用途 |
|---|---|
| AppBackground | アプリ全体の背景 |
| PanelBackground | パネル背景 |
| Border | 境界線・出力待機ボタン |
| Accent | アクセント・情報文字の基準 |
| SecondaryText | 補助文字・説明文 |
| IconFallback | 文字色を継承できない場合のアイコン色 |
| Warning | 警告・未適用の変更 |
| Success | 正常・適用成功 |
| OutputActive | 外部出力中のボタン |
| PlayingCue | 再生中のキュー背景 |

## 派生色と確認事項

ホバー・押下・選択・音声ONなどの派生色はThemePaletteが基本色から不透明なRGB値として生成する。混色率と各色の導出元はコードを基準とし、この手順には数値を重ねて掲載しない。基本色は不透明な6桁の色コードで指定する。

配色を変更したら、通常・ホバー・押下・非フォーカスの選択、再生中の青いハイライト、警告・エラー、音声ON/OFF、出力待機・出力中の見分けやすさを確認する。明るい配色へ大きく変える場合は、Fluent側の既定色との組合せも確認する。

## 画面からの参照

AXAMLでは `{DynamicResource Palette.SecondaryText}`、C#では `ThemePalette.SecondaryText` のように用途名で参照します。Colorが必要なリソースでは `Palette.SecondaryTextColor` を使用できます。共有ブラシを画面側から変更せず、状態に応じて参照先を切り替えます。

標準コントロールはAvalonia Fluentのダークテーマを使用し、アクセント色を共通定義から取得します。標準テーマが提供する通常の文字色・背景色はFluent側の既定値です。

映像キャンバスの黒、蓋絵の合成背景、透明指定は映像表示上の意味があるため、UI配色から除外しています。

## 実装の参照先

[ThemePalette.cs](../../src/LtcPlayer.Avalonia/ThemePalette.cs)

[保守手順の索引](README.md)・[今回の整理記録](../history/records/2026-11-20_009_SPEC_IMPLEMENTATION_REORGANIZATION.md)

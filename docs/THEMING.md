# UIの配色

`src/LtcPlayer.Avalonia/ThemePalette.cs` 冒頭の「基本色」10個を編集します。ホバー・押下・選択などは自動生成するため、個別に指定する必要はありません。変更後は再ビルドしてください。実行中のテーマ切り替え機能ではありません。

| 基本色 | 用途 |
|---|---|
| AppBackground | アプリ全体の背景 |
| PanelBackground | パネル背景 |
| Border | 境界線・出力待機ボタン |
| Accent | アクセント・選択状態・情報文字の基準 |
| SecondaryText | 補助文字・説明文 |
| IconFallback | 文字色を継承できない場合のアイコン色 |
| Warning | 警告・未適用の変更 |
| Success | 正常・適用成功 |
| OutputActive | 外部出力中のボタン |
| PlayingCue | 再生中のキュー背景 |

## 自動生成する色

- ホバーと押下：Accentを白に30%・55%近づけます。
- 控えめなアクセント：AccentをPanelBackgroundに25%近づけます。
- 選択背景：PanelBackgroundをAccentに35%近づけます。ホバー45%、非フォーカス22%、非フォーカスでのホバー32%です。
- 音声ONの背景：PanelBackgroundをAccentに18%近づけます。
- プレビュー周囲：PanelBackgroundとBorderの中間色です。
- 情報・ステータス文字はAccentHover、説明文はSecondaryText、出力待機ボタンはBorderを共用します。

混色は不透明なRGB値を生成します。基本色を変更すると関連色も追従しますが、明るい配色に大きく変更する場合は文字や選択状態の見やすさを確認してください。基本色は不透明な6桁の色コードで指定します。

## 画面からの参照

AXAMLでは `{DynamicResource Palette.SecondaryText}`、C#では `ThemePalette.SecondaryText` のように用途名で参照します。Colorが必要なリソースでは `Palette.SecondaryTextColor` を使用できます。共有ブラシを画面側から変更せず、状態に応じて参照先を切り替えます。

標準コントロールはAvalonia Fluentのダークテーマを継続使用し、アクセント色を共通定義から取得します。標準テーマが提供する通常の文字色・背景色はFluent側の既定値です。

映像キャンバスの黒、蓋絵の合成背景、透明指定は映像表示上の意味があるため、UI配色から除外しています。

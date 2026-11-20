# 設定の継続適用・未適用表示

2026-11-11

- 「適用」は設定を反映してパネルを開いたままにする。「閉じる」で終了する。
- 適用成功時に、その値を次の編集の基準に更新。引き続き編集・再適用できる。
- 未適用の項目は入力欄の枠線とラベルを橙色にし、変更のあるタブに「●」を表示。下部に「未適用の変更があります」を表示する。
- 値を適用済みの値へ戻すと強調を解除。変更がなければ「適用」を無効化する。
- 適用成功後は強調を解除し「適用しました」と表示。失敗時は未適用の内容とエラーを残して再試行できる。
- 適用中は二重実行と編集を防止。閉じる操作は適用済みの値を戻さず、残った未適用の編集だけを破棄する。
- LTC入力・音声出力の再読み込みボタンを、既存Lucideの円形矢印アイコンへ変更。ツールチップとアクセシビリティ名は再読み込みの対象を示す。
- 現行の設定タブ、入力レベル監視、表示方式の注意書き、出力中の設定変更制限を維持した。

## 検証

| 対象 | 結果 | 証跡（artifacts/redesign/配下） |
|---|---|---|
| アイコン・変更強調／復元・継続／反復適用・保存・失敗と再試行・二重適用・閉じる動作 | 6/6 PASS | apply-final-settings-apply/results.json |
| 表示方式の適用と出力中の制限 | 6/6 PASS | apply-final-output-mode/results.json |
| 画面サイズ・設定画面 | 8/8 PASS | apply-final-responsive/results.json |
| 設定・四辺画素・再生／LTC連続性・FPS | 8/8 PASS | apply-final-ui-corrections/results.json |
| 設定内の入力レベル監視・表示終了時のタイマー停止 | 6/6 PASS | apply-stays-input-level/results.json |

計34項目通過。警告0・エラー0、差分形式チェック通過。未適用表示の画面画像も確認した。

確認用ビルド：`C:\Users\r-izumi\Desktop\LTC_Player\artifacts\redesign\settings-apply-win-x64\LtcPlayer.Avalonia.exe`

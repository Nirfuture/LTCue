# 入力レベルを現行の設定タブへ移動

2026-11-11

- メイン画面のLTC入力ピークメーターを撤去。
- 現行の「設定 → LTC入力」タブに、入力ピーク・dBFS・受信状態を追加。タブ・配色・入力部品・配置の既存デザインを維持し、モックの外観や上部受信状態から開く操作は追加していない。
- 既存の入力監視を参照し、設定を開いても入力・動画・音声出力を再起動しない。表示更新は設定を閉じると停止。
- 映像出力中で入力設定が無効でも、レベルの監視表示は利用可能な構造にした。
- デバイス・チャンネルの未適用変更がある場合、表示レベルは適用中の入力であることを明示。

## 検証

| 対象 | 結果 | 証跡（artifacts/redesign/配下） |
|---|---|---|
| レベル換算・設定内ライブ表示・メインからの撤去・狭い設定画面・エラー時リセット・閉じた後のタイマー停止 | 6/6 PASS | meter-settings-input-level/results.json |
| 画面サイズ・設定画面の回帰 | 8/8 PASS | meter-settings-responsive/results.json |
| 設定・描画四辺・再生／LTC連続性・FPS | 8/8 PASS | meter-settings-ui-corrections/results.json |

計22項目通過。警告0・エラー0、差分形式チェック通過。設定の標準サイズ・狭いサイズの描画画像も確認した。実際の入力機器を使った音量の校正は未実施。

確認用ビルド：`C:\Users\r-izumi\Desktop\LTC_Player\artifacts\redesign\input-level-settings-win-x64\LtcPlayer.Avalonia.exe`

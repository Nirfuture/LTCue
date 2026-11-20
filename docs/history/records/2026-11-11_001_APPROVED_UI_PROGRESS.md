# LTC Player 合意モックのUI実装

2026-11-11

## 反映した内容

- 「プレビュー」「音声出力」「蓋絵」を、見出し・背景・境界・余白で分離した。
- 映像直下にシーク・時刻・動画ファイル名・再生方式／スキップ数を配置。再生情報の折りたたみと「次の動画」を除去した。
- Empty等の内部状態名を表示しない。準備・エラー等、必要な情報を表示する。
- 上部に「LTC受信中／LTC補間中／LTC信号喪失／LTC入力エラー／LTC待機」を表示。プレビュー内の「LTC同期」は再生方式として維持した。
- 音声出力ON/OFFはスピーカーボタン一つへ統合。別のミュート操作を除去した。音量0は時計を停止せず、OFFでは音声資源を解放する。
- 音声出力先・更新・音量・状態、蓋絵画像の変更・解除・表示切替・フェード秒数を整理した。
- 横長はキューと右パネルの2列。縦長・狭い幅は縦積み。高さだけでは3列化・縦積み化せず、足りない内容はスクロールで到達できる。
- キューのドラッグ・有効チェック・確認再生／停止・開始TC・長さ・fps・形式と既存の編集操作を維持した。
- Lucideのベクターアイコン11種類を同梱し、Avaloniaで描画。スピーカー、設定、再生／停止、ドラッグ、画像、解除、蓋絵切替、更新に使用した。ライセンスと元SVGは `src/LtcPlayer.Avalonia/Assets/Lucide/` に保存した。

Avalonia Fluentを使用し、合意したダーク配色と領域分けを反映。入力欄・コンボボックス等にはAvaloniaの実際の部品を使うため、HTMLモックと部品の細部は一致しない。

## 検証

| 対象 | 結果 | 証跡（artifacts/redesign/配下） |
|---|---|---|
| 単一音声ボタン、常時表示の再生情報、1640×940・1640×500・700×1100・480×420の配置 | 5/5 PASS | approved-final-ui-design/results.json |
| レスポンシブ配置・設定画面・画面移動 | 8/8 PASS | approved-ui-responsive/results.json |
| 共通映像・手動/LTC切替 | 13/13 PASS | approved-ui-playback-integration/results.json |
| 音声と映像の独立・LTCジャンプ／喪失／復帰 | 6/6 PASS | approved-ui-audio-connected/results.json |
| 音声UI・保存・停止確認・音量0・OFF | 7/7 PASS | approved-final-audio-ui/results.json |
| シーク位置・進捗寸法・フェード・準備／再開 | 7/7 PASS | approved-final-preview-ui/results.json |
| Coordinatorの停止・世代・終了競合 | 6/6 PASS | approved-final-coordinator/results.json |

計52項目通過。ビルド警告0・エラー0。実音声試験はWASAPIをゲイン0で使用した。レイアウト画像を保存し、実素材を読み込んだ表示も確認した。画像のネイティブ映像部分はAvaloniaのオフスクリーン描画に含まれないため黒になる。

描画確認で、SVGの各パスを連結すると相対座標が干渉する問題を発見。各パスを独立して描画するよう修正した。時刻表示の行高も明示した。

## 確認用ビルド

`C:\Users\r-izumi\Desktop\LTC_Player\artifacts\redesign\ui-approved-win-x64\LtcPlayer.Avalonia.exe`

既存のアプリや旧配布先は上書きしていない。実機出力先の切断・実LTC・長時間運転・物理的な映像と出音の遅延測定は引き続き別工程として残る。

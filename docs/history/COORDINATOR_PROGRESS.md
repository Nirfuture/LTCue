# LTC Player PlaybackCoordinatorへの集約

2026-11-11

## 実装

`PlaybackCoordinator`を追加し、次の責務をMainWindowから移した。

| 責務 | 所有者 |
|---|---|
| LTC優先判定・手動再生の許可判定 | PlaybackCoordinator |
| 同期セッションID・観測値・プログラム改訂・開始／停止・再試行待ち | PlaybackCoordinator |
| 同期再生のキャンセル・タスク・映像解析キャッシュ | PlaybackCoordinator |
| 手動／LTCから音声用プレイヘッドへの変換・世代照合 | PlaybackCoordinator |
| 音声サービスの生成・設定・破棄 | PlaybackCoordinator |
| 映像復号・手動再生の時計 | PreviewPlayer / ProgramPlayback（既存を維持） |
| 画面操作・設定保存・表示・映像出力のAttach/Detachと停止確認 | MainWindow（継続） |

Coordinatorは画面コントロールや設定ファイルへアクセスしない。MainWindowは操作・設定を渡し、観測値を表示する。音声用通知を作るためにMainWindowのUI更新処理へ依存する構成を外した。

同期ライフサイクルをコマンドゲートで直列化し、音声の設定・破棄には別のゲートを設けた。停止時には現在の同期セッションを先に無効化し、その後タスクをキャンセル・待機する。観測通知は現在の所有セッションと照合し、終了済みの通知を採用しない。共有状態はロックで保護し、音声スレッドの読み取りと分離した。

終了が音声設定と重なっても終了後に音声を開き直さず、破棄後の設定は拒否する。不要になった`PlaybackAudioTimeline`とMainWindow内の同期タスク・キャンセル・音声プレイヘッド管理を除去した。

## 検証

| 対象 | 結果 | 証跡（リポジトリ相対、artifacts/redesign/配下） |
|---|---|---|
| セッション・世代、改訂、同時停止、急速な開始停止、入力障害、終了と音声設定の競合 | 6/6 PASS | coordinator-final-coordinator/results.json |
| 手動/LTC・映像出力の統合 | 13/13 PASS | coordinator-final-playback-integration/results.json |
| 共通音声と手動/LTC接続 | 6/6 PASS | coordinator-final-audio-connected/results.json |
| プレビューUI | 7/7 PASS | coordinator-checked-preview-ui/results.json |
| 音声UI | 7/7 PASS | coordinator-checked-audio-ui/results.json |
| 基本再生・編集・同期の既存回帰 | 12/12 PASS | coordinator-checked-phase5/results.json |
| 映像出力操作 | 9/9 PASS | coordinator-delivery-corrected/results.json |

計60項目通過。実WASAPIを使う音声試験はゲイン0で行った。製品版・検証版とも警告0・エラー0で発行成功。差分形式チェックも通過。

回帰検証で、180ms周期の進捗タイマーがしきい値直前に発火すると次の周期まで表示が遅れる揺らぎが再現した。しきい値は180msのまま、確認周期を50msへ変更して再検証した。

出力操作試験の初回呼び出しは必須の`--media`がなく、試験開始前に終了した。素材と未接続画面の初期設定を指定して再実行した。また、パネル全体を隠す旧試験条件を「音声操作を表示し、シークは無効」に更新した。失敗記録も保持している。

## ビルドと残作業

確認用製品ビルド：リポジトリ内 `artifacts/redesign/coordinator-win-x64/LtcPlayer.Avalonia.exe`。既存のアプリや配布先を上書きしていない。

今回の集約は再生の調停とライフサイクルが対象。PreviewPlayerとProgramPlaybackの復号処理まで一つに作り直してはいない。旧音声経路は移行用の回帰試験に残り、通常起動では使用しない。

次は旧経路・試験・設計文書の整合を整理し、実機の出力先切断と終了経路、負荷時の復旧を確認する。長時間運転、実LTC入力、物理的な映像と出音の遅延測定は引き続き未完了。

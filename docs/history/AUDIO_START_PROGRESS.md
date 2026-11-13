# 音声ポン出しの開始遅延改善

2026-11-11

## 原因と変更

音声のデコード開始位置に、初回の再生開始でも150ms以上の将来位置を指定し、その時刻が来るまで音声を開始しない処理が入っていた。先読み幅が後続の開始にも引き継がれ、反復時に待ち時間が増える場合もあった。

通常の開始・再開・キュー切替は現在位置から準備し、将来位置を指定する先読みは、大きな同期ずれからの復旧時だけに限定した。

準備後に最新のセッション・キュー・世代・現在位置を再確認し、古い音声を捨てる契約は維持。動画時計、映像の開始待ち、UI、音声出力デバイス・バッファ量は変更していない。

## 同一条件での比較

音声出力をONにしてデバイスが開いている状態から、再生位置0のプレイヘッドを発行。各素材を3回ずつ測定した。実WASAPIをゲイン0で使用し、物理的な出音は測定していない。

| 素材 | 修正前・音声開始 | 修正後・初回 | 修正後・反復 |
|---|---:|---:|---:|
| H.264/AAC | 178〜206ms | 78ms | 30〜31ms |
| HAP/PCM | 258〜285ms | 54ms | 30〜32ms |

「音声開始」はサービスがWASAPIを開始してPlayingへ遷移するまでの時間。修正後の消費位置が実際に進むまでの時間は、初回約61〜87ms、反復約35〜38msだった。デバイス内部の遅延やスピーカーからの実際の音とは別の測定。

初回には素材解析とFFmpeg起動の時間が残る。今回の修正は不要な時刻待ちの除去であり、事前復号やキャッシュ追加による先頭音声の完全な準備までは行っていない。準備中は映像を止めず、準備完了時の現在位置へ追従する。

修正前：`artifacts/audio-v06/start-before/latency.json`

修正後：`artifacts/audio-v06/start-after/latency.json`

## 回帰検証

| 対象 | 結果 | 証跡（artifacts/audio-v06/配下） |
|---|---|---|
| 音声追従・世代・停止・低速準備・エラー復旧 | 8/8 PASS | quick-start-audio-service/results.json |
| 手動/LTCへの音声接続 | 6/6 PASS | quick-start-audio-connected/results.json |
| 映像・手動/LTC・出力切替 | 13/13 PASS | quick-start-playback-integration/results.json |
| 再生ライフサイクル・終了競合 | 6/6 PASS | quick-start-coordinator/results.json |

計33項目通過。比較計測は変更前後それぞれ6試行。警告0・エラー0でビルド。

確認用ビルド：`C:\Users\r-izumi\Desktop\LTC_Player\artifacts\redesign\audio-start-win-x64\LtcPlayer.Avalonia.exe`

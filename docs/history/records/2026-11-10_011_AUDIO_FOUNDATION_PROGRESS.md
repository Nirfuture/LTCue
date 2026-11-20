# 音声出力v0.6：基盤実装・段階Aの検証記録

2026-11-10。音声出力要件v0.6と実装計画の段階Aに対応。

## 到達点

出力先を明示した音声Sink、デバイス時計の観測、旧バッファの破棄、世代付きPCM、速度微調整の入口を実装した。無音の実WASAPI検証12項目が通過した。

共通再生への音声接続、音声出力UI、起動時オフ、LTC同期した実素材の音声再生はまだ実装していない。通常アプリは現行の確認用音声方式を維持する。単純に映像出力時の音声停止だけを取り除く変更はしていない。

## 追加した構成

- `AudioOutputSink`: 選択した実デバイスIDへ固定。全ストリーム操作を所有ワーカースレッド上で行う。
- `AudioPcmBlock`: CueId、世代、先頭PTS、ステレオサンプルを持つ。旧世代、別キュー、不連続PTSをWASAPIへ投入しない。
- `AudioDeviceSnapshot`: 投入・消費サンプル、デバイス位置と周波数、QPC、待機量、容量、速度倍率を観測する。
- `AudioSynchronizationPolicy`: 微小誤差を速度で補正し、大幅なずれを再シークへ分ける候補制御。準備中は再起動要求を重ねない。
- ネイティブAPIを追加。従来の`pa_open`と既存の確認用音声ABIを維持した。

## 検証条件

- 新音声基盤の実デバイス試験はストリーム音量0で実施。システムや他アプリの音量は変更していない。
- 出力機器8件を列挙し、その時点の既定出力先1件で実ストリームの試験を行った。全8機種で再生を検証したわけではない。
- 合成PCMを供給し、実WASAPIのキューと時計を観測した。聴感・音響的な同期精度の試験ではない。
- 開発確認SDKは10.0.203。リポジトリ指定10.0.403での配布用再現確認は未実施。

## 結果

| 試験 | 結果 |
|---|---|
| 出力列挙と実既定デバイスの解決 | PASS |
| 明示したデバイスID固定・存在しないIDで別機器へ転送しない | PASS |
| 入力デバイスを出力先として指定した場合の拒否 | PASS |
| 所有スレッド以外からのストリーム操作拒否 | PASS |
| Reset後のキュー・時計のクリア、旧世代・別キューの遮断 | PASS |
| 連続再生中のデバイス時計とQPCの整合 | PASS |
| 実デバイスでの速度倍率変更 | PASS |
| 微小ドリフトの収束と再起動抑止（シミュレーション） | PASS |
| 実デバイスでの微小誤差制御 | PASS |
| LTC前進・後退・FREEWHEEL・LOST・復帰・ERRORの状態遷移 | PASS |
| 遅い再同期準備を時計進行だけで再起動しない | PASS |
| 10回の開閉・リセット後に音声ストリームが残らない | PASS |

### 測定値

- 速度倍率0.99: 消費サンプル時間／ホスト経過時間 **0.9894**。
- 速度倍率1.01: 同じ比率 **1.0088**。各約2秒の測定で、調整方向の違いを確認。
- 25msの初期ずれを与えた実デバイス制御: 前半平均誤差 **16.70ms**、後半平均誤差 **7.08ms**。約2.5秒、再シーク0回。
- 30秒相当のシミュレーション: 補正なしの最終誤差 **55.0ms**、補正あり **2.00ms**、再シーク0回。実時間30秒の耐久試験とは区別する。
- キュー容量4800フレーム、48kHzで100ms。試験は容量まで充填して動作を確認した。製品の待機量目標を100msへ確定したものではない。
- デバイス時計の周波数は384000であり、48000と仮定せず、位置／取得周波数で時刻へ換算した。
- この出力先が報告したGetStreamLatencyは0。物理出音遅延が0という意味には扱わず、遅延補正値としての採用は保留する。

短時間・単一環境の測定である。補間・ドリフト制御の方式が成立する根拠として使い、全機器・実LTC・物理A/V同期精度の保証には使わない。

## 制御パラメーターの候補

- 2ms以内は不感帯。
- 100msを超えるずれは再シーク候補。世代・キュー変更はこの閾値を待たず旧データを破棄する。
- 通常速度補正は±0.5%以内。実デバイスの方向確認試験では±1%も使用した。
- 再シークの要求間隔は最短1秒。準備中は時計が進んだだけで準備を捨てない。

これらは段階Bの実素材・開始PTS・負荷試験へ持ち込む初期候補であり、最終的な同期品質の合格値ではない。

## 既存回帰と発見した不具合

既存の音声・シーク・音量・停止保持12項目、共通再生13項目、同期／遅延復旧7条件を再確認した。

最初の既存回帰で、同期中のPlay要求を拒否する際にPreviewPlayerがError状態へ変わり、共通再生エラー処理が外部映像を停止させる問題を検出した。操作拒否と再生障害を分離して修正し、再実行では25項目すべて通過した。初回の失敗記録も保存している。

## 結果・再現手順

- `artifacts/audio-v06/foundation/results.json`: 新基盤12項目。
- `artifacts/audio-v06/foundation/measurements.json`: 時計・キュー・速度・誤差の測定。
- `artifacts/audio-v06/foundation/devices.json`: このPCの出力先。機器IDを含むため共有時は内容を確認する。
- `artifacts/audio-v06/regression/phase5`: 初回の失敗記録。
- `artifacts/audio-v06/regression-fixed`: 修正後の既存回帰。
- 検証用ビルド後に`./scripts/legacy/verify-audio-foundation.ps1`で新音声基盤を再実行できる。

ランタイム同梱EXEと同スクリプトでも12項目すべて通過した。結果は`artifacts/audio-v06/packaged-foundation`へ保存。

通常版`artifacts/win-x64`への不具合修正の反映は、利用中アプリによるDLLのロックで完了していない。アプリの終了は行わず、修正済みの通常仕様ビルドを`artifacts/audio-v06/win-x64`へ別途作成した。このビルドにも新しい音声出力UIはまだ含まれない。

## 次の段階

1. AAC／PCMの実素材へPTS付き復号を接続し、音声ストリーム開始時刻差・途中オン・シークを検証する。
2. 共通プレイヘッドに音声Sinkを接続する。現行の音声消費量へ依存する映像時計を整理する。
3. 外部映像開始時の音声停止を撤去し、映像On/Off×音声On/Off×手動/LTCを検証する。
4. 音声出力先・On/Off・音量・ミュートのUIと保存を実装する。

未実施: 実デバイスの抜き差し、既定出力先の変更を伴う運用、別機器・他の環境、実LTC音声での長時間再生、音声と物理画面の遅延測定。無効IDでの拒否試験を、実デバイス切断試験の代わりにはしていない。

## API仕様の確認元

- [IAudioClock.GetPosition](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclock-getposition): 取得周波数による位置換算とQPCの単位を確認。
- [IAudioClient.Reset](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-reset): 停止後の待機データ破棄と時計リセットを確認。
- [IAudioClockAdjustment](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nn-audioclient-iaudioclockadjustment): 共有モードとRATEADJUSTによる微調整の条件を確認。
- [IAudioClockAdjustment.SetSampleRate](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclockadjustment-setsamplerate): リアルタイム処理スレッドで呼ばない制約を確認。

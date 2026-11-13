# 対応環境・ファイル形式

## 動作環境

| 項目 | 対応 |
|---|---|
| OS | Windows 11 x64 |
| GPU | Direct3D 11、BC1・BC3に対応したハードウェアGPU |
| LTC入力 | Windowsの音声入力機器（WASAPI共有モード） |
| 映像出力 | PCに接続したディスプレイ |
| 必要なソフトウェア | Visual C++ v14 x64ランタイム、FFmpeg・ffprobe |

映像はアスペクト比を保って表示され、余白は黒帯になります。.NETランタイムを同梱するため、利用者による.NET Desktop Runtime・SDKの追加インストールは不要です。

## 動画形式

| ファイル | コーデック |
|---|---|
| MP4 | H.264 |
| MOV | HAP、HAP Q |

- HAP alphaには対応していません。
- HAPは縦・横ともに4の倍数、正方形ピクセルの素材を使用してください。
- 主な動作確認は1080pで行っています。高解像度の素材は、実際に使用するPCで再生を確認してください。

## LTC形式

- 24 fps
- 25 fps
- 29.97 fps NDF
- 29.97 fps DF
- 30 fps

送信元とアプリのLTC形式を一致させてください。逆方向のLTCには対応していません。

## 音声

動画音声は手動再生・LTC同期のどちらでも出力できます。外部映像のON/OFFとは独立し、起動時はOFFです。AAC・PCM系、WASAPI共有モード、48kHzステレオに対応します。音声出力先は設定で選択し、ON時の機器へ固定します。

## 動作確認の範囲

現在はプレビュー版です。preview.11では2台のPCで開始時間を計測、外部ディスプレイの物理切断・再接続も限定的に確認しています。

2時間以上の連続稼働、物理画面と出音の同期精度は未確認です。各版の確認範囲は[確認記録](releases/0.1.0-preview.11.md)、開始時間は[検証環境A](https://github.com/Nirfuture/LTCue/blob/main/docs/STARTUP_CHECK_ENV_A.md)・[検証環境B](https://github.com/Nirfuture/LTCue/blob/main/docs/STARTUP_CHECK_ENV_B.md)を参照してください。

次の環境・機能も未検証です。

- リモートデスクトップ・仮想環境
- HDR・色管理
- 複数GPU
- 可変リフレッシュレート
- 8K素材の実用性能

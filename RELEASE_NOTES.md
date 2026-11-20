# LTCue 0.1.0

LTCueの初回プレビューリリースです。Windows 11 x64向けのLTC同期映像プレイヤーです。

## 主な機能

- LTCに合わせた動画再生と、外部ディスプレイへの映像出力。
- MP4/H.264、MOV/HAP・HAP Qに対応。
- 動画ごとの開始TC・有効/無効設定、キューリストの保存・読込。
- 手動再生、一時停止、停止、シーク、動画音声の確認。
- 蓋絵の表示・フェード切替。
- 素材エラーの表示

## 起動と制限

ZIPをフォルダーごと展開し、LTCue.exeを起動します。.NETランタイム同梱。Visual C++ v14 x64ランタイムとFFmpeg/ffprobeは別途必要です。

[クイックスタート](docs/guides/QUICKSTART.md)・[対応環境](docs/guides/COMPATIBILITY.md)・[困ったとき](docs/guides/TROUBLESHOOTING.md)

音声出力機能は確認用です。素材や環境によって再生準備に時間がかかる場合があります。

実LTC機器、長時間耐久、物理画面・スピーカーの同期は未確認です。確認範囲は[確認記録](docs/releases/0.1.0.md)を参照してください。

未署名のため起動が制限される場合があります。MANIFEST.jsonで配布物のソースコミットを確認できます。

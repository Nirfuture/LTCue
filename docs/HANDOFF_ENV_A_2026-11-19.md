# 環境A：検証完了と再生開始遅延調査の引き継ぎ

2026-11-19。次のチャットでは本資料と[環境Aの検証記録](ENV_A_VERIFICATION_2026-11-19.md)を先に読む。

## 現在地

- リポジトリ：`C:/Users/izumi/Desktop/LTCue/LTCue`。指定ワークスペース直下より1階層深い。
- リモート：`https://github.com/Nirfuture/LTCue.git`。
- 作業ブランチ：`test/env-a-output-presentation-20261119`。
- 基点：`fix/output-presentation`、`7cc237245eb2f056b3077bc1ec7fbbe1a09db5fd`。ユーザー記載のアンダースコア版ではなく、実在するブランチ名はハイフン。
- ユーザーは検証側の修正を承認済み。今回、検証修正・記録・本引き継ぎをこの検証用ブランチへcommit・pushするよう依頼。元ブランチやmainへのマージは依頼されていない。
- 製品の再生・同期処理や設定形式は変更していない。変更は検証コード、素材生成、ランナー、資料のみ。

## 検証結果と利用する実行ファイル

正式な`scripts/test.ps1`で55スイートすべてPASS、終了コード0（2026-11-19 18:39 JST完了）。ランナー集計550項目は性能サンプル等も含み、独立した単体テスト550個ではない。通常版・検証版ビルド、ランナー保守9項目もPASS。

| 用途 | リポジトリからの相対パス |
|---|---|
| 通常利用する最終版 | `artifacts/env-a-final-v2/product/LTCue.exe` |
| 診断・検証用の最終版 | `artifacts/env-a-final-v2/app/LTCue.exe` |
| 最終一括証跡 | `artifacts/tests-env-a-final-v2-20261119/` |
| ソース・検証バイナリのSHA-256 | `artifacts/env-a-final-v2/source-manifest.json` |
| 通常版ハッシュ・完了記録 | `artifacts/tests-env-a-final-v2-20261119/completion.json` |
| ランナー保守証跡 | `artifacts/env-a-final-v2/runner-checks/` |
| LTC設定・状態表示を3回ずつ確認した証跡 | `artifacts/env-a-final-v2/recheck-1/`～`recheck-3/` |

通常版はビルド済み。実行には同じフォルダー内のDLL等も必要なため、持ち運ぶ場合はフォルダーごとコピーする。今回のコミット対象はソースと資料であり、ignoredな`artifacts/`内の実行ファイルや生ログはpushしない。別PCでは再ビルドまたは生成物の別途転送が必要。

実LTC、物理AV同期、実機抜き差し、実素材の全尺性能、2時間耐久は未実施。自動合格をそれらの合格や、再生開始が速いことの保証と扱わない。

## 検証側で修正したこと

1. TC編集の実テスト32項目に対し、ランナーが31項目を期待していた更新漏れ。
2. 設定保存・読込3スイートが、プリセット配列をrecordの参照等値で比較していた。検証専用ヘルパーで全設定のJSON値を比較する。値や配列順の違いを拒否する確認も追加。
3. 出力方式試験が固定TCで複数提示を要求し、同じフレームの重複提示を省く製品処理と矛盾していた。進む合成TCへ変更。
4. 縦置き試験が撤去済みのスキップ数ツールチップを要求していた。現行仕様へ期待値を更新。
5. LTC状態表示試験の6秒素材が喪失・復帰の途中で終了していた。映像・音声付き30秒素材を追加し、ランナーの必須素材とハッシュ記録にも追加。
6. 初回同期・復帰の準備待ちだけ6秒から15秒へ変更。製品の遅延しきい値・継続時間は維持し、実待ち時間を`waits.json`に記録。
7. LTC設定試験で新セッションIDと画面側の古い観測・音声状態を組み合わせて準備完了とする競合。現在のセッション自身の映像観測・音声プレイヘッド・新しい音声状態を待つよう修正。許容差・資源維持条件は維持。

途中の失敗と再試験は検証記録に保存。最終55 PASSは同じ正式一括実行の結果であり、複数試行を合算した結果ではない。

## PCと導入環境

- 環境A：Intel Core i9-14900KF（32論理CPU）、RTX 4090、ドライバー31.0.15.3667、高パフォーマンス電源プラン。
- DISPLAY1：1080×1920縦置き。DISPLAY2：3840×2160横置き。画面別試験は両方PASS。
- BIOSのCPU Turbo Mode / Intel Turbo Boost無効はユーザー申告。BIOS・電源・セキュリティ設定は変更していない。
- SDK 10.0.203をMicrosoft公式インストーラーでPCへ通常導入。`C:/Program Files/dotnet/dotnet.exe`を使用。既存SDKは保持。ユーザー領域には別途10.0.403もあるが、プロジェクトの`global.json`は10.0.203固定・rollForward無効。
- FFmpeg / ffprobe：8.1.2 full build（gyan.dev）。`%LOCALAPPDATA%/Programs/FFmpeg/8.1.2-full/ffmpeg-8.1.2-full_build/bin/`。ユーザーPATHへ登録済み。既存Codex/ターミナルは旧PATHを継承している場合があるため、次の計測でも実行パスを確認する。
- リポジトリ専用に一度配置したSDK・FFmpegは、通常導入へ切り替えるユーザー指示に従って削除済み。`artifacts/tools/dotnet-install.ps1`は開始前から存在したため保持。

## 再生ボタンから映像開始までの遅延：調査は未完了

ユーザーは通常版の再生開始が遅いことを報告。原因はまだ確定しておらず、製品側の速度改善は行っていない。

### 今回実測した範囲

ユーザー依頼で`ffmpeg -version`を6回だけ実行した。動画ファイルの読込・復号なし。設定変更・キャッシュ消去なし。PowerShell 7.6.9 / .NET 10.0.14から、`Process.Start`（UseShellExecute=false、CreateNoWindow=true、stdout/stderrリダイレクト）で起動した。

| 回 | Start復帰まで ms | 終了まで ms |
|---|---:|---:|
| 1 | 565.67 | 611.73 |
| 2 | 550.87 | 591.34 |
| 3 | 560.99 | 601.38 |
| 4 | 559.74 | 600.11 |
| 5 | 549.75 | 589.50 |
| 6 | 540.61 | 580.35 |

全回終了コード0。実行バイナリSHA-256：`AD8F211BC894755E0061C55AB280AE00E8D3D4F15A8CC4372B24CFA247B5942E`。生データ：`artifacts/ffmpeg-version-20261119-184745/results.json`。

起動API復帰まで約0.54～0.57秒が繰り返される事実は確認したが、純粋なWin32プロセス生成時間やセキュリティ検査時間を分離した測定ではない。通常版の同梱.NETランタイムは10.0.7であり、このPowerShell経由の値をそのままアプリのProcess.Startの実測値とは扱わない。

### HAPとの関係を混同しない

ユーザーの指摘どおり、最新HAP映像のフレーム復号はFFmpegプロセスを使わない。

- `src/LtcPlayer.App/MediaDecoder.cs`：素材情報取得はffprobe。MOV/HAPは`HapMediaDecoder`へ分岐し、H.264映像復号はFFmpeg。
- `src/LtcPlayer.App/HapMediaDecoder.cs`：MOVのサンプルテーブルを`MovHapIndex`で読む。対応外レイアウトのみffprobe索引へフォールバック。ファイルからパケットを読み、`HapCodec.Unpack`で展開してGPUへ渡す。
- `src/LtcPlayer.Avalonia/Playback/AudioOutputService.cs`ほか：音声ONでは音声解析・復号にffprobe / FFmpegを使う。

従って`ffmpeg -version`が約0.6秒かかることだけでは、HAP映像の開始遅延は説明できない。Turbo Boost無効やPC故障が主因とする証拠もない。

### 次に行う最小の調査案（まだ実行しないまま引き継ぎ）

同じHAP素材で音声OFF、外部出力OFFから始め、要求受理→メタデータ→索引（キャッシュ・フォールバックを区別）→読込・展開→最初の提示を計測。初回と停止後の再生を分ける。音声ONと出力ONは後から1条件ずつ追加する。クリック入力からの遅延と、内部再生メソッド受理後の時間も区別する。

既存の`--verify-startup-measure`と`Playback/StartupMeasurementTrace.cs`を確認する。入口は`StartupMeasurementVerification.cs`。現在の入口は音声ON・ハードウェアゲイン0・外部出力OFF・合成WAITING・初回＋停止後2回に固定されているため、そのままでは音声OFF比較ができない。LG ULTRAFINEがあれば選び、なければ既定音声出力へフォールバックし、縦画面を優先する。必ず`conditions.json`を照合する。

検証版の既存入口を利用する場合の形（音声ON固定の診断で、音声OFF測定ではない）：

```powershell
& ./artifacts/env-a-final-v2/app/LTCue.exe --verify-startup-measure ./artifacts/startup-new-run --settings ./artifacts/startup-new-run/settings.json --media C:/path/to/local-hap.mov
```

結果フォルダーは毎回新規。実素材のパスはユーザーからまだ指定されていない。過去資料の`E:/denkaFes/...`がこのPCに存在すると仮定しない。手元の同梱HAPは診断入口の確認には使えるが、ユーザーが遅いと感じた素材そのものの実測とは区別する。

過去の[環境A開始計測](STARTUP_CHECK_ENV_A.md)、[プロセス起動調査](STARTUP_PROCESS_LAUNCH_ENV_A.md)は参考。資料内の旧コミットID・旧ランタイム・素材・出力条件を今回と混同しない。まず現状の工程計測を行い、BIOSやセキュリティ設定の変更を前提にしない。

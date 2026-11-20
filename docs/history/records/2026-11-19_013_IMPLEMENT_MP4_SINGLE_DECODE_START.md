# 次セッションへの実装指示：MP4初回手動再生のFFmpeg二重起動の解消

## 1. この文書の位置付けと依頼

この文書を作成したセッションでは、指示書の作成と既存資料・コードの読取だけを行い、今回の改善の製品実装・ビルド・性能計測には着手していない。以下は**次の実装セッションで行う作業**である。過去の計測値・検証結果は変更前の参考証跡であり、今回の変更後の合格を示すものではない。

MP4/H.264の初回手動再生で、先頭静止画取得と連続再生のために映像用FFmpegが二重起動する問題だけを改善する。既存`FrameBuffer`を、同一の明示的なMP4再生要求内で先頭提示から連続再生へ引き継ぐ方式を第一候補とする。

主な短縮対象は、**先頭静止画の提示後から、映像時計の開始・連続再生開始までの待ち時間**である。最初のFFmpeg起動は残るため、最初の静止画そのものの提示が早くなることは保証しない。

PTSと再生時計の対応関係、表示内容、再生中の提示方針は維持する。再生要求から開始までの実時間は短縮対象であり、「タイミング維持」を開始待ち時間まで固定する意味に解釈しない。

最初にコードと同条件の実測で二重起動の理由を確認し、所有権・取消・PTSを簡潔に保てると判断できた場合だけ実装する。複雑な状態管理や同期精度の低下が避けられない場合は、無理に統合せず、調査結果と実装を見送る理由を報告する。

commit・push・merge・releaseは行わない。

## 2. 作業場所、開始時点、既存変更の保護

ワークスペースの1階層下がリポジトリである。

```powershell
Set-Location C:/Users/izumi/Desktop/LTCue/LTCue
git status --short
git branch --show-current
git rev-parse HEAD
```

文書作成時点のブランチは`perf/mov-pcm-formats-20261119`、HEADは`c62cd541eccd9a9f492d60bfc4fe4d1e2c9d9db4`。**作業の基点はHEAD単独ではなく、現在の多数の未コミット変更を含む作業ツリー**である。素材情報共有・MOV/PCM形式拡張・低水準MOV処理共通化・試験・資料・4分素材は未コミットである。次セッション開始時に現在の状態を再照合すること。

特に次は文書作成時点で未追跡なので、`git diff`だけでは保全できない。

- `src/LtcPlayer.App/MediaAnalysisService.cs`
- `src/LtcPlayer.App/MovAtomReader.cs`
- `src/LtcPlayer.Avalonia/Playback/MovPcmReader.cs`
- `src/LtcPlayer.Avalonia/DirectPcmVerification.cs`
- `src/LtcPlayer.Avalonia/MediaAnalysisVerification.cs`
- `docs/history/records/2026-11-19_012_MOV_PCM_FORMATS.md`、直近の調査・引継ぎ資料と本書
- `TestMedia/LongTest-20261119-233910/`
- `TestMedia/LongTest-FullHD-20261119-234052/`

開始時の全status・ブランチ・HEAD・binary diff、変更済み/未追跡のソース・資料の復元可能なコピーとSHA-256を、新しい証跡ルートへ保存する。大きい生成動画はパス・生成スクリプト・既存manifest・ハッシュを保存し、元ファイルを変更しない。既存変更と今回の変更を完了報告で区別する。

現在の作業ツリーを保持したまま専用ブランチを作る。候補名は`perf/mp4-single-decode-start`。同名がある場合は内容を確認し、無関係なら別名にする。reset・clean・既存変更の破棄・無差別なstashを行わない。Gitが所有者の違いで拒否する場合は、必要に応じてこのリポジトリに限定した`git -c safe.directory=C:/Users/izumi/Desktop/LTCue/LTCue ...`を使用し、グローバル設定を不用意に変えない。

稼働中の通常版を終了・上書きしない。他の作業の検証版も勝手に終了しない。他の検証版が動いている場合は検証開始を待ち、その理由を通知する。自分が起動した検証プロセスを終了する場合も、保持したPIDと開始情報で自分のプロセスツリーに限定する。

文書作成時の探索では適用対象の`AGENTS.md`は見つからなかったが、次セッションでは親ディレクトリ・リポジトリ・変更対象配下を再確認する。

## 3. 先に読む仕様、実装、証跡

相対パスはすべてリポジトリルート基準。

### 作業ルールと現行仕様

- `docs/development/FINAL_SPEC.md`
- `docs/development/implementation/OPERATION_ACCESS.md`
- `docs/development/BUILD.md`、`docs/development/VERIFICATION.md`
- `docs/development/implementation/HAP_PREPARATION.md`
- `docs/history/records/2026-11-19_008_SHARED_MEDIA_ANALYSIS.md`
- `docs/history/records/2026-11-19_012_MOV_PCM_FORMATS.md`
- `docs/history/records/2026-11-19_010_AUDIO_LAUNCH_INVESTIGATION.md`

過去の改善計画にある別段階の提案を、今回の範囲拡張の指示として扱わない。

### 主な実装

- `src/LtcPlayer.Avalonia/MainWindow.Delivery.cs`：`PlayManualAsync`、行の▶、素材採用とUI側の権限再確認
- `src/LtcPlayer.Avalonia/MainWindow.OperationAccess.cs`：手動操作の許可条件
- `src/LtcPlayer.Avalonia/PreviewPlayer.cs`：`LoadAsync`、`PlayAsync`、`FirstFrameAsync`、`RunAsync`、`HaltAsync`、停止・シーク・所有権切替・破棄
- `src/LtcPlayer.Avalonia/Playback/FrameBuffer.cs`：8枚の有界キュー、ワーカー、取消と破棄
- `src/LtcPlayer.App/MediaDecoder.cs`：MP4のFFmpegデコード、HAP分岐、PTS取得とプロセス終了
- `src/LtcPlayer.Avalonia/Playback/PlaybackCoordinator.cs`：LTC優先権、`PublishManual`、共有音声の準備許可
- `src/LtcPlayer.Avalonia/Playback/ProgramPlayback.cs`：既存LTC再生とストリーム上限の確認
- `src/LtcPlayer.Avalonia/Playback/AudioDecodeSession.cs`、`AudioOutputService.cs`：音声は変更対象ではないが、共存契約を確認
- `src/LtcPlayer.Avalonia/StartupMeasurementVerification.cs`、`Playback/StartupMeasurementTrace.cs`
- 関連する`*Verification.cs`、`scripts/verification-suites.json`、`scripts/test.ps1`、`scripts/verification-common.ps1`

### 直近の証跡

- `artifacts/mp4-start-diagnostic-20261119-234745/REPORT.md`
- 同ルートの`summary.json`、`metrics.json`、各条件の`transitions.json`、`conditions.json`、`samples.json`、`hashes.json`
- `artifacts/mov-pcm-formats-20261119-230859/implementation-plan.md`、`final-audit.json`、`source-manifest.json`
- 同ルートの`acceptance/summary.json`、`acceptance/manifest.json`、`final-app/`、`product/`

前回の正式一括は57スイート・ランナー集計598項目PASS。件数には性能サンプル等も含み、独立単体試験数ではない。今回の変更後にも最終ソースから正式一括をやり直す。

`artifacts/mp4-start-diagnostic-20261119-234745/`、`artifacts/mov-pcm-formats-20261119-230859/`、`artifacts/audio-launch-20261119/`、`artifacts/shared-media-20261119/`、その他既存の証跡・バイナリを再利用出力先にして上書きしない。

## 4. 現状の処理経路と二重起動の理由

### 4.1 初めての素材を明示的に手動再生する経路

現状の`MainWindow.PlayManualAsync`はおおむね次の順で動く。

1. `play_request`を記録し、手動操作権限を確認する。
2. 同期プレビューを停止し、再び権限を確認する。
3. `LoadedVersionMatches`、キューID、パスで同じ素材か判定する。
4. 別素材またはrestartの場合、`Preview.LoadAsync`を呼ぶ。
5. 読込成功後、`previewCue`、素材エラー、`Editor.Validate()`を更新する。
6. LTC優先権・同期所有権を再確認する。
7. 同一素材の再生中▶は一時停止。それ以外は`Preview.PlayAsync`へ進む。

`LoadAsync`は旧再生を`HaltAsync`し、共有情報解析後に`FirstFrameAsync(file, 0, token)`を呼ぶ。`FirstFrameAsync`はワーカー上で`MediaDecoder.DecodeAsync`を列挙し、最初の1枚を返す。このreturnで列挙子を破棄するため、MP4の復号プロセスも終了する。取得したフレームを提示し、`Ready`状態と素材情報・世代を公開してLoadが完了する。

続く`PlayAsync`は別の`FrameBuffer`を作り、そのワーカーが同じMP4について`MediaDecoder.DecodeAsync`を新たに開始する。最初の1枚を読み、提示してから`clock.Restart()`し、**そのFrameBuffer自体は既に`RunAsync`へ引き継いでいる**。未統合なのは、その前のLoad側の静止画取得との間である。

MP4の`MediaDecoder.DecodeAsync`はFFmpegを起動し、scale/pad/setsar/showinfo、BGRA stdout、showinfoのPTSを用いる。返却する`VideoFrame.Seconds`は現行の`seconds + seekSeconds`。この時刻計算・フィルター・デコード方式を高速化のために変えない。

### 4.2 二重起動は異なる寿命のAPIを連結した結果

Loadは「素材確認と1枚提示を完了し、復号を保持せずに戻る」操作で、Playは「連続再生用のバッファを所有する」操作である。そのため現在の初回手動再生では、両方の独立した準備が直列に実行される。

単純に`FirstFrameAsync`を「プロセスを閉じない」実装に置き換えると、単独Loadやシーク、停止時の先頭表示にも保持寿命が広がる。これは今回の方針に反する。明示的な再生要求を入口に統合できるかを検討すること。

停止後の同一素材再生ではLoadを省き、通常は既存の先頭フレームを利用するため、映像用FFmpegは元から1回。停止後のプロセス起動待ちを解消する作業は今回含まない。一時停止からの再開も、既存のloopが生存する場合の挙動を維持する。

### 4.3 変更前の参考実測

直近の検証版`artifacts/mov-pcm-formats-20261119-230859/final-app/LTCue.exe`で、登録済み同版素材を手動再生した結果。単位はplay_request基準のms。要求前の登録時間は別記録。

|4分Full HD MP4|先頭静止画提示・初回|映像時計開始・初回|最初の連続フレーム提示・初回|停止後4回の時計開始中央値|停止後4回の連続提示中央値|
|---|---:|---:|---:|---:|---:|
|音声OFF|722.0|1405.1|1447.9|675.6|713.2|
|音声ON|706.7|1388.4|1428.5|675.0|711.3|

- 初回の映像用FFmpegは2回、停止後は1回。
- AAC音声ONでは音声用が別に1回加わり、全FFmpegは初回3回、停止後2回。音声Startは初回1874.8ms、停止後中央値1159.3msだった。
- 720pも映像時計開始が初回約1.33秒、反復中央値約0.64秒で、起動待ちが大きい。
- Full HD HAP/24bit PCM MOVは全FFmpeg起動0、時計開始初回106.3ms・反復中央値4.0ms。

音声ON時は映像・音声の起動要求がほぼ同時でも、音声のProcess.Start区間が約1.1秒だった。OS内部の詳細原因は未特定。FFmpegの起動呼出しと復号処理の時間を混同せず、セキュリティ設定等は変更しない。

これらはLG ULTRAFINE音声出力、1080×1920キャンバス、専用設定、ハードウェアゲイン0、外部出力OFF、合成LTC WAITINGでの内部API時刻である。物理的な表示・出音時刻、全環境の保証値ではない。

## 5. 対象と対象外

### 対象

- 同一の明示的なMP4/H.264初回手動再生要求における、Loadの先頭提示からPlayの連続再生への引継ぎ。
- そのために必要な最小限の入口・バッファ所有権・取消・採用判定の整理。
- 追加の対象試験、最小限の検証専用計測、同条件の前後比較、最終候補の正式回帰、別出力先への通常版ビルド。
- 別素材への初回手動切替やrestartが同じ入口を通る場合の適用可否の明示と回帰確認。

### 対象外

- Media Foundation、libavcodec/libavformat等への移行、独自H.264デコーダー。
- FFmpegオプション、復号画質、スケール、ハードウェアデコード、スレッド数等を変える別のMP4高速化。
- AAC/MP3復号の改善、音声プロセス保持、音声PCMキャッシュ、先行起動。
- 停止・別要求・次回再生まで生きる新しい映像プロセスやバッファの保持、汎用プリロード機構。
- 単独Load、行選択、停止後の先頭表示、シークへの新しいプロセス保持の拡張。
- HAP/HAP Q、MovHapIndex、MovAtomReader、MovPcmReaderのデコード・索引・形式判定の変更。
- LTC時計、同期補正、フレーム選択方針、GPUキャンバス、外部出力、音声デバイス、設定/保存形式、UIの機能変更。

今回だけでMP4の停止後・AAC音声を含めた全起動待ちがなくなるとは主張しない。

## 6. 実装前に確定する設計と安全条件

変更前のソースとバイナリを保全し、適用条件、変更する入口、所有者の遷移、取消・失敗経路、比較基準を先に新規証跡へ記録する。

第一候補の流れは次のとおり。ただしAPI名や分割方法を先に固定する指示ではない。

1. 明示的なMP4再生要求に対し、必要な情報解析と旧再生の停止を行う。
2. 既存`MediaDecoder`を使った`FrameBuffer`を1個作り、同じ要求がこれを所有する。
3. 先頭フレームを一度取り出し、権限・取消・素材版・要求世代を確認して提示する。
4. UI側で必要な素材採用・エラー解除・検証と、最終的な再生許可を確認する。
5. 同じ先頭フレームとFrameBufferを再利用し、先頭を再復号・再キュー投入・意図しない再提示をせずに、時計開始と`RunAsync`へ引き継ぐ。
6. 引継ぎ前に拒否・取消・失敗した場合は準備側、成功後は再生側が確実に破棄する。

先頭フレームは既存の`firstFrame`/`last`等と整合させ、停止後の先頭表示にも必要な範囲で従来どおり利用できること。**新しい長期キャッシュを追加する指示ではない。**

設計時に必ず確認する事項：

- **所有者は各段階で1者だけ。** 準備中→採用→再生中の引継ぎを明確にし、二重Dispose、未所有、参照を失ったワーカーを作らない。拒否時に次の再生へ持ち越さない。
- **取消トークンの寿命。** 現Loadのローカル`operation`はfinallyで破棄され、`preparation`も外される。これをそのまま持続バッファへ付けるだけでは、停止・LTC取得時の取消先を失う恐れがある。要求全体と再生中の寿命を整合させる。
- **ロックの再入禁止。** `PreviewPlayer.commands`は非再入のSemaphoreSlim。保持したまま、同じgateを取得する既存public Load/Playを呼んでデッドロックさせない。
- **UI側の採用境界。** 現在の`previewCue`更新、`row.ClearSourceError()`、`Editor.Validate()`、その後のLTC再確認を読み、統合で飛ばさない。待機中・通知中にLTC優先権や対象が変わる場合も、古い要求のPlayを許可しない。
- **状態と音声許可。** MediaSessionId、PlaybackSessionId、SourceGeneration、Ready/Playing、PreparingPlayback、PreparationCancellation、IsBusyの公開を整合させる。共有音声へ旧素材・旧世代を公開しない。音声準備の完了を時計開始条件に追加しない。
- **素材版・寸法。** 同じパスだけで引き継がず、ファイル版・キュー・要求・実際のデコード寸法を確認する。待機中の素材変更やキャンバス寸法変更で使えなくなった準備は破棄し、安全に拒否または明示的な再準備とする。古い寸法のバッファを黙って採用しない。
- **先頭と連続フレーム。** 先頭を取り出したために次のフレームを欠落させない。同じ先頭を再投入して二重消費させない。静止提示後の同一内容保持と、新たな提示API呼出し/提示数増加を区別する。通常の時計追従による既存のフレーム間引き方針は変えない。
- **有界性・背圧。** FrameBufferの8枚キューを維持する。先頭提示後、キューが満杯になって生産側が待っていても取消・破棄が完了すること。全長先読み、別の無制限キュー、大量フレームコピーを追加しない。
- **ワーカーとUI。** 既存FrameBufferのワーカーで起動・復号し、同期的Process.StartやI/O、ワーカー終了待ちをUIスレッドへ持ち込まない。
- **デコーダーとHAP。** 共用クラスを触る場合でもHAPの経路・索引・所有権は変えない。MP4以外に統合経路を広げない。

短命な要求内の所有権整理で収まらず、複数要求にまたがる永続状態、新しい同期補正、先頭PTSの付け直し、取消の不確実化が必要になる場合は実装を見送り、理由と代案を報告する。簡潔で安全な方法が見つかった場合は、通常の実装判断のたびに確認を求めず、認められた範囲内で検証まで進める。

## 7. 維持する既存契約

- 行選択は一覧選択だけを変え、素材解析・静止画・HAP索引・音声・映像バッファの準備を開始しない。
- 単独の素材読込は1枚提示後に新たな連続プロセスを保持しない。
- 同一素材の再生中▶による一時停止、一時停止後の再開、停止/終端後の先頭再生、手動シークと同期中の操作拒否を維持する。
- 停止は1回で位置0と先頭表示、終端は最後の提示を保持する。蓋絵・外部出力の開始停止によって再生源や時計を不必要にリセットしない。
- LTC優先権、喪失/復帰、ジャンプ、世代による旧フレーム・旧音声の拒否、素材切替・削除・リスト置換時の解除を維持する。
- 初期PTS、非ゼロ開始やBフレームを含む既存FFmpeg経路の時刻解釈を維持し、先頭PTSを0に作り直さない。
- 共有素材解析はウィンドウ/セッション所有、完成結果最大8件。素材版の照合、変更・エラー・明示再解析時の無効化を維持する。
- 映像ストリーム数の既存上限（現在・先読み/復旧を合わせ最大2）を維持する。準備バッファを隠して第3の復号窓を追加しない。
- 音声復号窓1、音声FFmpeg最大1、PCM1024フレーム×8ブロック、LTC基準の音声追従を維持する。映像用と音声用のプロセス数は別に数える。
- 停止・取消・失敗・終了時のプロセス、stdout/stderr読取、ワーカー、CTS、ファイルハンドル、フレーム参照の解放を維持する。
- 通常版には検証CLI・計測コードを混入させない。

## 8. 比較計測

### 8.1 前後で同じ素材・バイナリ基点・条件を使う

古いHEADやMOV/PCM改善前の版へ戻さず、次セッション開始時の現行実装を変更前基準としてビルド・保存する。既存の診断バイナリとソースの一致が確認できる場合も、使った版と根拠を記録する。計測だけを追加する場合は、前後に同じ計測を入れ、機能変更前の計測バイナリを保存する。

既存の4分素材：

|素材|相対パス|
|---|---|
|Full HD MP4|`TestMedia/LongTest-FullHD-20261119-234052/test-4min-fullhd-h264-aac.mp4`|
|720p MP4|`TestMedia/LongTest-20261119-233910/test-4min-h264-aac.mp4`|
|Full HD HAP/PCM MOV（非影響の対照）|`TestMedia/LongTest-FullHD-20261119-234052/test-4min-fullhd-hap-pcm24.mov`|

いずれも240秒・30fps・7200枚、経過時間/フレーム番号表示、48kHz stereo。MP4はH.264/AAC、MOVはHAP/signed24 LE PCM。各素材フォルダーの`manifest.json`、`generate.ps1`、ffprobe JSONがある。元素材を上書きせず、前後のハッシュ一致を確認する。短い正式fixtureやHAPデフォルト4素材も回帰用に使う。

### 8.2 計測項目

`StartupMeasurementVerification`と既存マーカーを可能な限り再利用する。前後でFull HD/720p MP4の音声OFF/ON、各独立起動の初回1回＋停止後4回、対照MOVを同条件で測る。別素材への初回切替と既出素材への復帰も、固定順序を決めて前後同一にする。初回と停止後・切替・復帰を合算しない。

- 再生要求：`play_request`
- 先頭静止画/先頭フレーム提示：`video_first_present`
- 映像時計開始：`video_clock_start`
- 最初の異なる連続フレーム提示：`video_playback_first_present`
- 主な差分：要求→各段階、先頭提示→時計開始、先頭提示→最初の連続フレーム提示
- 映像用FFmpegの起動要求・実起動数・可能なら起動所要時間、音声用FFmpeg、情報用/索引用ffprobeを分離
- 登録/リスト読込・情報解析・バッファ準備の時間。待ちを別段階へ移して隠さない
- 最初のPCM、WASAPI投入、Start復帰は音声の参考値として別に保存。AAC改善の合否と混ぜない
- 各試行のPTS/提示フレーム、開始後の内部同期差、プロセス/復号窓、フレームキュー、メモリ/ハンドル、停止/破棄後の資源

現在の汎用`decode_ffmpeg_start/started`は音声・映像の両方を含む。現行音声ON初回の「3→2」は映像「2→1」と音声1回の合計である。音声起動を映像起動に数えない。並列起動の要求/復帰を時刻順に単純zipして、別プロセスのProcess.Start時間を結び付けない。必要なら検証専用の要求ID・役割・PIDを追加して対応を取る。

統合後も`video_first_present`を時計開始に付け替えない。既存のOnceマーカーだけでは先頭の二重提示を検出できないので、別の要求内フレーム/PTS観測で検証する。

専用検証版、独立設定、ハードウェアゲイン0、同じキャンバス・デバイス・LTC条件で実施する。外部出力OFFの性能値とONの機能回帰を混ぜない。OS cache・他アプリ負荷・初回標本数などの限界も記録する。改善量は実測で判断し、約550ms削減等を事前保証しない。

正常な対象要求では、映像FFmpeg初回2→1が構造上の目標。音声OFFなら合計2→1、音声ONなら通常は合計3→2。停止後の既存1回が増えないこと。取消・素材変更・寸法変更による正当な再準備は別集計し、失敗や不要再起動を隠さない。

## 9. 対象試験と正式回帰

実装前に試験項目と予定件数を確定し、実装後に少なくとも次を確認する。

1. MP4の先頭静止画と連続再生が同じ要求/復号プロセス/バッファに由来し、映像起動1回になる。
2. 最初のフレームを1回だけ消費し、連続側で次のフレームを飛ばさず、意図しない二重提示・提示数増加をしない。フレーム番号入り素材やデコード画像のハッシュ、PTSで確認する。
3. 変更前と先頭の実フレーム・PTS・後続列が一致する。提示スケジューリングの揺らぎと、復号列の欠落を区別する。通常のH.264/Bフレーム、短い素材、既存の非ゼロ開始素材等でも時刻を付け直さない。
4. 準備中の取消、先頭提示後〜時計開始前の取消、満杯キューでの停止、即停止、再生連打、失敗/EOF/破損、終了で確実に解放する。
5. 待機中/引継ぎ直前のLTC取得、世代変更、素材切替、素材版変更、寸法変更で旧結果が採用されない。
6. 同一素材の一時停止/再開、停止/終端後の先頭再生、シーク中の再生/停止、途中位置、末尾、繰返し切替を維持する。シーク等の二重起動を今回の別の改善対象へ広げない。
7. 単独Loadは戻った後に新しい連続プロセス/バッファを保持せず、行選択は準備を始めない。
8. 音声OFF/ON・デバイス失敗/明示復旧、音声準備取消、映像時計と音声PTSの関係、LTC優先・喪失/復帰を維持する。
9. HAP/HAP Qの先頭/中間/最終フレーム、索引共有・追い出し・無効化、既存PCM6形式、素材差替え、共通キャンバス/外部出力に回帰がない。
10. 反復停止/再開と素材切替後のプロセス/ワーカー/フレーム保持、ハンドル・メモリ・上限を確認する。GC後heap、Working Set、Private Bytesは分けて記録する。

取消の競合は固定sleepだけに依存させず、必要最小限の検証専用フックで段階を待ち、対象の旧要求と新要求を識別して検査する。追加検証コードは通常版から除外する。

最終候補は`docs/development/VERIFICATION.md`に従い、**正式`scripts/test.ps1`の全スイートを単一実行で完走**させる。開始時点は57スイート。追加時はカタログと予定件数を整合させる。途中の個別PASSを合算して全件合格にしない。検証カタログ一致、証跡の存在・形式・予定件数・全項目合否・終了コードを確認する。

計算系4DLLも必ず最終ソースからビルドする。`-SkipBuild`では今回ビルドした次の4本のパスをすべて明示し、既定の古いDLLを拾わない。

- `SyncRecoveryAssemblyPath`
- `HapPreparationAssemblyPath`
- `JumpRecoveryAssemblyPath`
- `TimecodeEditingAssemblyPath`

ランナー本体/共通検証判定を変更した場合は、`scripts/test-verification-runner.ps1`の保守用誤判定防止試験も行う。期待値・精度・資源上限・期限を、合格のために緩和しない。先頭の意図しない二重提示削減に伴う正当な提示数期待の更新は、理由と新しい検査を記録する。

実LTC、物理デバイス抜差し、物理表示/出音遅延、他環境、2時間耐久は自動試験と別扱いにし、未実施を合格扱いしない。

## 10. ビルド環境と実行の目安（次セッション用）

.NETは`C:/Program Files/dotnet/dotnet.exe`、SDKは`global.json`の10.0.203（rollForward disable）。MSVC/Windows SDK、PowerShell 7.2以降を使用する。

FFmpeg/ffprobeの通常導入先：

```text
C:/Users/izumi/AppData/Local/Programs/FFmpeg/8.1.2-full/ffmpeg-8.1.2-full_build/bin/
```

旧PATHに注意し、実際の解決先・版・ハッシュを前後で保存する。サンドボックスから導入先やWindows検証環境へアクセスできない場合は、認められた昇格手順を使い、理由を明示する。バイナリの置換やOS保護設定の変更で起動待ちを回避しない。

出力先は例えば`artifacts/mp4-single-decode-start-<新規識別子>/`。毎回新規であることを確認してから使う。以下のコマンドは実装セッションでの参考であり、指示書作成セッションでは実行していない。

```powershell
# $evidenceは今回専用の新規出力先、$dotnetは上記の絶対パスとする。
./scripts/build.ps1 -Verification -DotnetPath $dotnet -OutputPath "$evidence/before-app"
# 実装と対象試験後、最終候補も新規出力先にビルドする。
./scripts/build.ps1 -Verification -DotnetPath $dotnet -OutputPath "$evidence/final-app"

foreach ($name in 'SyncRecovery','HapPreparation','JumpRecovery','TimecodeEditing') {
  & $dotnet build "tests/$name/$name.csproj" -c Release -o "$evidence/calculations/$name"
  if ($LASTEXITCODE -ne 0) { throw "Calculation build failed: $name" }
}

./scripts/test.ps1 -SkipBuild -DotnetPath $dotnet `
  -ExecutablePath "$evidence/final-app/LTCue.exe" `
  -SyncRecoveryAssemblyPath "$evidence/calculations/SyncRecovery/SyncRecovery.dll" `
  -HapPreparationAssemblyPath "$evidence/calculations/HapPreparation/HapPreparation.dll" `
  -JumpRecoveryAssemblyPath "$evidence/calculations/JumpRecovery/JumpRecovery.dll" `
  -TimecodeEditingAssemblyPath "$evidence/calculations/TimecodeEditing/TimecodeEditing.dll" `
  -ResultPath "$evidence/acceptance"

./scripts/build.ps1 -DotnetPath $dotnet -OutputPath "$evidence/product"
```

実行時は各ネイティブコマンド/スクリプトの失敗を確認して処理を止め、stdout/stderr・終了コードをログへ保存する。正式ランナーのfixtureは既定で新規生成できる。既存素材を再利用する場合は`-FixturesPath`と`-AudioFixturesPath`の両方を指定して必須ファイルを照合する。4分素材は正式fixture一式の代用にはならない。

開始計測の入口は、検証版の次の引数である。

```text
--verify-startup-measure <新規結果フォルダー>
--media <素材の絶対パス>
--startup-audio off または on
--settings <その結果フォルダー内の独立設定ファイル>
```

現行ハーネスは初回＋停止後4回。`--startup-switch on`は既定でHAP4素材用なので、MP4切替では`--startup-materials <改行区切り絶対パス一覧>`も指定する。引数なしでMP44本用だと誤認しない。

通常版は検証CLIで起動して判定せず、PEメタデータによるカタログ不在と検証型不在等を確認する。同じソースから作ったこと、最終バイナリ・4DLL・素材・ソースハッシュの一致を照合する。

## 11. 作業順序と完了報告

1. ルール・状態・未追跡ファイルを確認し、開始時を保全して専用ブランチを作る。
2. 現行処理を追い、変更前の同条件計測と対象試験の基準を保存する。
3. 所有権/取消/採用の設計を先に記録する。安全に統合できなければ、実装せず理由を報告する。
4. 安全な場合だけ最小限を実装し、段階別取消・PTS・表示・資源の対象試験を通す。
5. 前後比較、HAP等の非影響確認、最終ソースからの正式一括、別フォルダーの通常版ビルド、最終照合を行う。
6. 初回/反復/切替/復帰の数値、途中失敗と修正、未実施・評価限界を分けて報告する。

完了報告には次を含める。

- ブランチ・基点・開始時未コミット変更と今回の変更の区別。
- 二重起動の理由、採用方式、適用境界、所有権/取消/失敗時の破棄先。
- 映像用・音声用・ffprobeを分けた前後起動回数、先頭提示/時計開始/連続提示とその間隔、登録・準備時間。
- 先頭/後続フレーム・PTS・時計対応、二重提示/欠落なし、停止/再開/シーク/LTC/素材切替/取消の検証。
- HAP/HAP Q・MOV/PCM経路の非影響、資源上限と解放、正式一括の単一実行結果、通常版ビルドと証跡。
- 改善しない範囲、ばらつき、未実施の実機試験。
- 確認用通常版実行ファイルの絶対パスと、同じフォルダーのDLL等が必要なこと。

実装見送りの場合は「完了した改善」とせず、安全に統合できなかった具体的な境界・必要になってしまう追加状態・試した/試していない範囲を報告する。いずれの場合も、他のMP4高速化、音声基盤変更、commit・push・merge・releaseへ自動で進まない。

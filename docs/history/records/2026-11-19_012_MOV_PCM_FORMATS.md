# 48kHz MOV/PCM形式拡張とMOV低水準共通化（2026-11-19）

## 基点と保全

ブランチ `perf/mov-pcm-formats-20261119`、HEAD `c62cd541eccd9a9f492d60bfc4fe4d1e2c9d9db4`。開始時は `perf/audio-launch-20261119`。HEADだけでなく、多数の既存未コミット変更を含む現行直接読取版を基点とした。開始時の全status、binary patch、変更済み/未追跡ファイルの復元コピーとSHA-256は証跡ルートの `baseline/`。既存通常版・以前の証跡は上書きしていない。commit/push/merge/releaseなし。

今回の変更は MovAtomReader新規追加、MovHapIndexの低水準処理委譲、既存未追跡MovPcmReaderの形式拡張、DirectPcmVerificationの拡張、StartupMeasurementVerificationの任意素材リスト対応、Avalonia/HapPreparationのコンパイル参照、カタログの予定項目数と関連仕様文書。既存共有解析・音声生産/出力・同期・UIの未コミット実装は保全。差分全体を今回の実装とは扱わない。

## 対応範囲

|サンプル形式|MOV表現|mono frame bytes|stereo frame bytes|
|---|---|---:|---:|
|signed16 LE|sowt / version 0 / packed 16有効bit|2|4|
|signed24 LE|in24 / version 1 / packed 24有効bit / wave enda=1|3|6|
|IEEE float32 LE|fl32 / version 1 / packed 32bit / wave enda=1|4|8|

48kHz・ゼロ開始・単純な編集・自己完結した非分割MOV・映像と整合した音声長に限定。monoは既定mono、stereoは既定FL/FR。chan省略または標準Mono/Stereo tag・bitmap0・description0だけを受ける。v1の旧sampleSize=16/bytesPerSample=2を格納幅と誤認せず、samplesPerPacket=1、bytesPerPacket=3/4、bytesPerFrame=格納幅×channelsを照合する。MOVのstsz sampleが1 PCM時間フレーム、stts delta=1の場合だけを受ける。wave内frmaはtagと一致、endaは1、その他は空の終端atomのみ。

v2/lpcm、BE、padding、planar、異なるpacket構造/拡張、signed32、多ch、非48kHz、複雑な編集・時刻差は未対応でFFmpegへ戻す。全MOV表現の網羅ではない。切断atom、範囲外offset、時刻表/チャンク表の件数不整合等は破損拒否。正常BE/signed32も実エンコードでフォールバックを検査した。

## 共通化と寿命

共有したのは数値読取、atomヘッダーと32/64bit/サイズ0・境界検証、moov/mdat探索、共通テーブル件数の基本検証、自己完結データ参照検証。呼出元の例外生成とmetadata上限、重複moov方針を渡す。ヘルパーはストリームを開閉せず、Avaloniaにも依存しない。HAPはFileShare.Read、PCMはReadWrite|Deleteを維持。

HAP索引/時刻/sample配列とPCMのchunk索引/時刻・形式判定は独立。stts/stscの意味解釈や採用条件の重複は、映像sampleとPCM時間フレームの責務が違うため残した。PCMは全フレーム配列を作らず、22秒（1,056,000frames）の実素材でもHAPの100万sample上限を誤適用しない。共有解析の8件、HAP索引4件/32MiB、復号窓1、FFmpeg最大1、1024frames×8blocksは維持。

## PCM比較

同じFFmpeg 8.1.2、AudioDecodeSessionの既存filterが基準。signed16/32768、signed24の符号拡張/8388608、float32は値を保持。monoは各出力へ0.7071067811865476fを掛け、左右と音量を基準波形と比較した。S16 stereoは従来1e-6未満、新形式は2e-7以下。長さ差は2frames以内、各blockのPTS/世代/IDと先頭位置・端数seek・末尾を照合。NaN/Infは通常差分計算へ流さず種類と符号を別検査、有限範囲外値も復号段で保持。native pa_writeのnonfinite→0・gain後clipは従来実装のまま。

## 前後計測

前=`before-measured`（現行の直接読取実装＋任意素材切替計測だけ追加）、後=`final-app`。各145試行、うち音声ON100試行。デフォルト4素材OFF/ON各初回+4回と1→2→3→4→1、追加5形式ON各初回+4回と5形式→先頭復帰の固定切替。前後素材/ツールハッシュ、条件、全試行の生値は証跡ルート。

LG ULTRAFINE、1080×1920 canvas、専用設定、ハードウェアゲイン0、外部出力OFF、合成LTC WAITING。音声デバイスは要求前初期化。OS cacheや他アプリ負荷は制御していない。以下は内部play_request→WASAPI Start復帰msで、物理的な出音ではない。初回は各条件1標本。

|素材|初回 前→後 ms|停止後4回 中央/最大 前 ms|停止後4回 中央/最大 後 ms|
|---|---:|---:|---:|
|default-1-on|109.3 → 106.9|8.6 / 11.4|9.2 / 12.7|
|default-2-on|106.2 → 105.4|7.8 / 8.6|7.1 / 9.1|
|default-3-on|106.9 → 105.5|7.4 / 10.3|6.9 / 7.7|
|default-4-on|113.1 → 108.8|6.9 / 8.1|7.6 / 10.9|
|pcm_s16le-1|713.4 → 106.0|603.6 / 608.6|7.1 / 8.0|
|pcm_s24le-1|695.5 → 105.6|604.5 / 607.4|6.8 / 8.6|
|pcm_s24le-2|718.2 → 105.0|603.7 / 605.1|7.5 / 9.2|
|pcm_f32le-1|709.6 → 100.3|600.0 / 601.7|7.9 / 8.6|
|pcm_f32le-2|687.8 → 100.0|602.7 / 605.7|7.5 / 8.1|

初回・停止後・初めての素材切替・既出素材復帰はcomparison.jsonのvisitsで別集計。再生要求・先頭提示・映像時計・連続提示・first PCM・first write・Start、登録/準備時間、起動要求/実起動/Process.Startも別項目を保存。

|固定切替|各訪問の初回音声Start 前 ms|後 ms|
|---|---|---|
|default-5-on|91.7 / 23.1 / 19.6 / 19.0 / 22.9|90.2 / 18.5 / 20.8 / 17.8 / 23.0|
|additional-switch|697.8 / 610.9 / 606.3 / 612.6 / 609.6 / 598.0|95.4 / 15.3 / 14.3 / 13.6 / 16.5 / 14.6|

|デフォルト連続映像提示|初回 前→後 ms|反復中央 前→後 ms|反復最大 前→後 ms|
|---|---:|---:|---:|
|default-1-off|117.7 → 115.6|47.3 → 47.2|48.4 → 56.3|
|default-2-off|100.5 → 111.4|38.1 → 40.3|46.5 → 41.8|
|default-3-off|104.6 → 105.2|40.3 → 39.2|46.6 → 45.2|
|default-4-off|118.4 → 110.5|47.2 → 41.1|58.4 → 45.7|
|default-1-on|108.5 → 107.2|40.3 → 39.1|47.3 → 39.8|
|default-2-on|101.1 → 127.9|39.1 → 38.7|40.0 → 39.9|
|default-3-on|105.9 → 104.5|39.3 → 38.5|42.0 → 45.2|
|default-4-on|125.9 → 108.2|39.9 → 40.5|40.3 → 46.7|

映像の数msの上下を一律の改善とは扱わない。既存高速経路の音声初回は同程度で、反復にも小さな上下がある。今回の主な差は追加形式のFFmpegプロセス省略である。

|条件群|音声FFmpeg実起動 前→後|要求後の情報/索引ffprobe 前→後|
|---|---:|---:|
|default|0 → 0|[0, 0] → [0, 0]|
|additional|55 → 0|[0, 0] → [0, 0]|

比較後の全145試行で要求後info/index ffprobeは0、音声ON100試行でaudio FFmpegは0。変更/追い出し後の正当な解析や対応外形式の起動はこの性能集計に含めない。

## 登録時間と資源

|条件|登録時間 前→後 ms|
|---|---:|
|additional-switch|3058.9 → 3049.9|
|default-1-off|683.6 → 680.2|
|default-1-on|671.0 → 678.8|
|default-2-off|713.2 → 711.5|
|default-2-on|739.0 → 684.7|
|default-3-off|666.3 → 672.2|
|default-3-on|672.0 → 675.0|
|default-4-off|692.7 → 699.4|
|default-4-on|702.9 → 689.4|
|default-5-off|2498.1 → 2454.9|
|default-5-on|2471.0 → 2471.8|
|pcm_f32le-1|691.6 → 681.2|
|pcm_f32le-2|671.5 → 670.4|
|pcm_s16le-1|782.2 → 688.4|
|pcm_s24le-1|681.0 → 690.7|
|pcm_s24le-2|683.1 → 682.3|

before: 準備中央値/最大 1.1/34.5ms。音声Process.Start中央値/最大 538.3/549.2ms。
アプリ観測（Audio Start後250ms）: PrivateBytes median/max=436942848/548073472, WorkingSet median/max=306407424/371761152, Handles median/max=1242/1264, DecodeWindows median/max=1/1, AudioProcesses median/max=1/1。
内部音声誤差絶対最大 8.22ms、reseeks最大 0。

after: 準備中央値/最大 1.1/28.9ms。音声Process.Start中央値/最大 該当なし。
アプリ観測（Audio Start後250ms）: PrivateBytes median/max=484388864/555450368, WorkingSet median/max=328239104/373800960, Handles median/max=1230/1233, DecodeWindows median/max=1/1, AudioProcesses median/max=0/0。
内部音声誤差絶対最大 8.96ms、reseeks最大 0。

アプリ全体のPrivate Bytes/Working SetはCLR・GPU・動画・GC状態も含む。音声開始が早くなると同じ観測時点の映像進行も異なるため、音声メモリ削減とは主張しない。GC後管理ヒープは別の音声専用反復で評価する。全計測の停止後decode/process=0、破棄後decode/process/stream=0を照合した。

## 検証と限界

最終の検証結果と通常版パスは後続の監査結果節を参照。途中失敗はincidents.mdと各build/testログへ分離。実LTC、物理デバイス抜差し、物理AV同期、他環境、2時間耐久は未実施。存在しない出力IDによる失敗注入と合成LTCを実機検証の代用にしない。非48kHzやMedia Foundation等の対象外作業へ進んでいない。

証跡: `artifacts/mov-pcm-formats-20261119-230859`。

映像の差には数～数十msの上下があり、特にdefault-2-onの連続提示初回は101.1→127.9ms（+26.8ms）だった。単一の初回標本から無悪化を保証しない。停止後4回の同条件中央値は39.1→38.7msで、音声初回は106.2→105.4ms。全条件の初回・反復・最大を残しており、この増加を除外していない。co64の試験は64bit表を使う実PCMだが、実offsetが4GiBを超える巨大ファイルは未試験。

## 最終監査結果

正式 `scripts/test.ps1` は `acceptance/` の単一実行で57スイート・ランナー集計598項目すべてPASS、終了コード0。独立単体試験の件数ではない。direct-pcmは26項目、6形式×5シークの30比較でPCM最大差0・サンプル数差0。特殊値/符号境界は別の合否検査に含む。元の14項目の無関係な閾値・期限は緩和していない。

計算系4DLLは最終ソースから `calculations/` に新規ビルドし、SkipBuildへ4本の絶対パスをすべて明示した。カタログ一致、実行DLL/素材ハッシュ一致、ソース200ファイルのハッシュ不変、開始時の保全コピー一致は `final-audit.json`。通常版はPEメタデータで検証カタログ不在・Verification型0を確認し、判定のために通常版を起動していない。ランナー本体は変更せず、カタログのPCM予定件数のみ14→26へ更新した。

HAP/HAP Qの5実素材で全packetの位置/サイズ/PTSがffprobeと一致（hap-real-comparison.json）。最終DLLを使った独立ハーネスで5素材×先頭/中間/最終frameの15点を読み出し、PTS・展開バイト数・SHA-256を記録してPASS（hap-real-first-middle-last.json）。Qの最終frameは0.96秒。従来scanのduration-1では短いQ素材の末尾確認にならないため、最終frame指定のハーネスで補った。

正常MOV27本をテスト対象の共通ヘルパーとは独立したPython atom読取とffprobeで確認し、サンプル記述生bytes・codec・サンプル形式・channels・rate・SHA-256をfixture-independent-audit.jsonへ保存。性能素材の生成コマンドはgenerate-fixtures.ps1、試験素材の実エンコード/生PCM生成コマンドはDirectPcmVerification.csと保存ソースにある。

### 音声のみの反復

最終DLLの実AudioOutputService、ハードウェアゲイン0、動画/描画なしで6形式を循環して200回停止/再開。FFmpeg起動0、最大復号窓1、PCM8blocks以下、各停止後window/process/hardware PCM=0、サービス破棄後stream=0。audio-resource-stress.jsonとresource-harnessに生値とコードを保存。

|測定時点|GC後管理heap bytes|Private Bytes|Working Set|handles|GC committed bytes|
|---|---:|---:|---:|---:|---:|
|0|406440|15712256|44085248|336|2646016|
|20|418376|20959232|50450432|336|5857280|
|200|421048|28459008|58720256|339|7507968|
|201|400608|27746304|58716160|322|7507968|

最初の採取は3回暖機後だが、この時点では全6素材の解析がまだ揃っていない。20→200回のGC後heapは418376→421048 bytes、handlesは336→339（140回以降339）。サービス破棄後heap400608 bytes、handles322。共有解析6件はハーネスscope終了前まで保持しており、この採取値は全解析破棄後ではない。スコープ終了時に解析サービスもDisposeされる。Private Bytesは増加しており、CLR/GC commitとnative予約を含めた完全無増加や長時間リーク不在を保証するものではない。

### リスト読込

|版|初回 ms|反復4回 中央/最大 ms|解析実行累計|
|---|---:|---:|---:|
|before|2382.2|2333.2 / 2350.1|20|
|after|2414.6|2339.1 / 2346.2|20|

同じ4素材の保存リストを5回明示読込・再検査。FFprobeの解析は5回×4素材で20回。リスト読込に待ちを移す変更はなく、改善保証もしない。list-harnessとlist-before/afterに保存。

### 映像初回の追加確認

元の145試行は変更せず、default-2-onだけ追加で前後各3独立起動・各5再生を行った（video-initial-check/、計30試行）。

|追加回|連続映像初回 前→後 ms|音声Start 前→後 ms|
|---|---:|---:|
|1|104.5 → 105.9|110.6 → 106.5|
|2|138.3 → 140.4|115.7 → 113.6|
|3|103.3 → 133.8|109.0 → 113.5|

前側にも138msの初回があり、追加標本でも約1frameのばらつきがある。後側の初回中央値が低いという結果ではなく、無悪化を統計的に証明したとは扱わない。映像時計・提示処理の方針は変更しておらず、正式のHAP/共有canvas/再生統合/復帰検証はすべて通過している。

### 音声FFmpeg自身の資源

変更前の追加形式では最初のPCM時点で音声FFmpegだけを採取。アプリのAudio Start後250ms観測とは同時刻でなく、OS全体の同時ピークとも異なる。

|観測|変更前 中央/最大|変更後|
|---|---:|---:|
|audio_ffmpeg_private_bytes|21745664 / 22216704|プロセスなし|
|audio_ffmpeg_working_set|22179840 / 22433792|プロセスなし|
|audio_ffmpeg_handles|313 / 315|プロセスなし|

### 通常版

`C:/Users/izumi/Desktop/LTCue/LTCue/artifacts/mov-pcm-formats-20261119-230859/product/LTCue.exe`

同じproductフォルダーのDLL・ランタイム・TestMedia等も必要。product-build.logに通常版scripts/build.ps1の成功を記録。通常版は起動していない。最終版ソース・バイナリ・素材のmanifestと途中失敗を含む全証跡は上記ルートに保存。

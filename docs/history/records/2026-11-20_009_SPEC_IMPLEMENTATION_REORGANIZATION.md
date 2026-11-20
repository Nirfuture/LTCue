# 現行仕様・実装補足・保守手順の整理

2026-11-20。[照合レビュー](2026-11-20_008_REFERENCE_SPEC_COMPARISON.md)に基づき実施。動作・操作条件は現行仕様へ、非同期処理の採用条件や資源の寿命は実装補足へ、配色は保守手順へ、試験の実行方法は検証手順へ整理した。アプリと試験コードの動作は変更していない。

## 配置対応

| 旧パス | 現在の資料 |
|---|---|
| `docs/reference/README.md` | [docs/development/implementation/README.md](../../development/implementation/README.md) |
| `docs/reference/OPERATION_ACCESS.md` | [docs/development/implementation/OPERATION_ACCESS.md](../../development/implementation/OPERATION_ACCESS.md) |
| `docs/reference/LTC_SETTINGS.md` | [docs/development/implementation/LTC_SETTINGS.md](../../development/implementation/LTC_SETTINGS.md) |
| `docs/reference/HAP_PREPARATION.md` | [docs/development/implementation/HAP_PREPARATION.md](../../development/implementation/HAP_PREPARATION.md) |
| `docs/reference/PLAYBACK_ERRORS.md` | [docs/development/implementation/PLAYBACK_ERRORS.md](../../development/implementation/PLAYBACK_ERRORS.md) |
| `docs/reference/SYNC_RECOVERY.md` | [docs/development/implementation/SYNC_RECOVERY.md](../../development/implementation/SYNC_RECOVERY.md) |
| `docs/reference/THEMING.md` | [docs/maintenance/THEMING.md](../../maintenance/THEMING.md) |

## 内容の整理

- FINAL_SPECの追記節を該当する機能節へ配置。操作・保存・入力切替・素材復旧の動作条件を一元化。
- 入力開始は選択済み機器のオープンとデコーダー生成で確認し、最大10秒。LTC到着を待つ条件とは区別。未選択は待機として扱う。
- 素材復旧の成立条件と限界、読込時のID修復・一覧維持を現行仕様へ補足。
- 画面表示のmax(50ms, 1.5フレーム)/0.3秒と、追従復旧のmax(200ms, 3フレーム)を分離。旧資料の「200msで画面表示」は現行表示仕様として扱わない。
- 補足は操作条件表の重複を省き、責務・世代・採用と確定の境界を保持。配色の混色率の一覧はコードに任せ、変更方法と確認事項を保持。

## 整理前の記述

以下は書換え・統合前の段落。現在の動作の基準ではなく、整理時点の原文を保存したもの。単純に移動して本文に残った段落も含む場合がある。日付は今回の整理日であり、各原文の作成・実施日を示さない。

### FINAL_SPEC.md

[現在の資料](../../development/FINAL_SPEC.md)

2026-11-14。0.1.0公開候補を対象とする。移行前のpreview.10の記録は[確認記録](../../releases/0.1.0-preview.10.md)を参照。現行仕様の基準は本書。旧要件・再設計案・進捗記録は経緯を示す資料とし、旧記述を読み替えて利用する必要がないよう、現在の動作をここにまとめる。旧仕様本文は[履歴](2026-11-10_002_FINAL_SPEC-before-preview.10.md)に保存する。

LTC受信に同期中（LOCKED）で再生対象・観測値が有効な場合に限り、再生情報の近くに「映像：遅延なし」「音声：遅延なし」または位置差を表示する。手動再生中は両方を「再生中」とし、内部の手動時計との位置差を同期状態として表示しない。FREEWHEELは「補間中」、LOST/WAITINGで同期映像を保持中は「入力待ち」、RESYNCは「再同期中」とし、遅延判定の正常表示へ進めない。映像の許容幅は50msと1.5フレーム分の大きい方、音声は50ms。許容幅を超えるずれが同じ方向へ0.3秒続くと「120ms遅れ／先行」のように10ms単位で表示する。復帰も0.3秒確認し、一時的な変化は「確認中」。準備中・補間中・入力待ち・再同期中・停止中・一時停止・OFF・音声なし・入力／描画エラーは別状態とし、未取得・古い音声測定値・旧世代の観測値を正常扱いしない。これは表示判定であり、既存の補正・復旧処理を操作しない。

自己完結した単純なMOVの48kHz・mono/stereo PCM（sowt version 0のsigned16 LE、in24/fl32 version 1のpacked signed24/IEEE float32 LE）はサンプル表から直接読み、その他の対応音声は従来のFFmpegで復号する。直接経路はゼロ開始・音声長と映像長が一致する等の条件に限定し、時刻シフト・複雑な編集・リサンプル等はFFmpegへ委ねる。停止後のPCMキャッシュやプロセス保持は行わない。

素材情報の解析結果はウィンドウ内の共通サービスで最大8ファイルを共有する（旧音声専用2件キャッシュを置換）。復号は1プロセス、PCMは8ブロック。音声準備・デバイス待ちを映像ループへ持ち込まない。WASAPI投入目標は30ms分で、微小ドリフトをレート補正し、大きいずれは再同期する。内部PTSと物理的な出音の遅延は別に測定する。

設定はLTC入力・音声出力・映像出力の3タブ。「適用」は閉じずに反映し、「閉じる」は未適用分だけ破棄する。変更欄・タブを強調し、変更がない場合は適用不可。機器更新は円形矢印。外部映像出力中もLTC設定を変更できる。補間・閾値は現在の時計を維持して次の受信フレームから適用し、補正は同期位置だけを更新する。入力機器・チャンネル・形式の切替は入力開始確認後に確定する。映像出力先・方式は外部出力停止時、音声出力先は音声OFF時に変更できる。

未接続の映像出力先を保持し、無関係な設定適用で先頭画面へ変更しない。映像出力先がない場合も入力・音声設定を適用できる。新しいキャンバスと保存ファイルを準備し、保存成功後に確定する。失敗時は旧キャンバス・蓋絵・設定を保ち、必要なら旧入力を復旧する。入力復旧は無瞬断ではない。適用失敗後に手動再生を勝手に再開しない。

上演JSONはVersion1、上限4MiB。相対素材パスはJSON位置を基準とし、読込時に再検査する。前回の上演を自動復元しない。設定は実行フォルダーのphase2-settings.jsonへ一時ファイル置換で保存する。

## 素材障害・復旧の設計

出力接続と同期再生可否を分離する。空の同期予定は正常な待機状態。素材の事前失敗は予定から除外し、実行開始後の失敗は黒・無音で次の正常キューまたは素材復旧を待つ。復旧時は現在LTCで再評価する。詳細と責務・制限は[素材エラー設計](../../development/implementation/PLAYBACK_ERRORS.md)を参照。


HAPの索引は対応する非分割MOVのサンプル表から構築し、範囲外の正常構造のみ既存ffprobe経路へ委ねる。素材版で識別する有界なセッション内キャッシュを手動・LTC・素材確認で共有し、映像・音声の実行状態を保管しない。素材情報は登録・リスト読込・プレビュー・共有音声で単一のffprobe結果を共有する。詳細と対応範囲は[HAPの準備](../../development/implementation/HAP_PREPARATION.md)を参照。

LTCジャンプでは旧実行の終了待ち後に最新位置を再評価する。移動先の準備は動画1フレーム分の手前から行い、固定200msの巻き戻しを行わない。提示直前と観測値の採用時に入力世代・権限を確認する。新しく採用していないフレームで保持画像のID・PTS・提示回数を更新しない。音声の独立追従と、継続的な遅延への既存復旧は維持する。

右側のプレビュー・音声・蓋絵はすべて内容に必要な自然な高さで上に寄せ、余白で伸ばさない。初期読込とレイアウト測定後、横並びでは右側の高さに合わせてウィンドウを一度だけ調整し、一覧との下端を揃える。画面の作業領域に収まらない場合は既存の画面内配置とスクロールを使う。

ユーザーがその後に高さを変更すると、一覧だけが余剰高さを使い、右側との下端の差を許容する。初期読込中に既にサイズが変更された場合も上書きしない。蓋絵の高さ上限や継続的な再計算は使用しない。狭幅・縦長の縦積み表示は従来の高さとスクロールを維持する。

## 表示と文言

選択行は白を混ぜた無彩色、表示中のキューのハイライトは青とする。ハイライトの条件と選択時の優先関係は維持し、新たな枠は追加しない。左下は「有効 X 件」「エラー Y 件」とし、有効はEnabledの行数、エラーはEnabledかつHasErrorの行数。エラーが1件以上のときのみエラー側を赤くする。

蓋絵操作は二枚重ねアイコンと「表示／非表示」で次の操作を示す。変更は画像追加アイコンと文字、解除は×。この2つは右端へ間隔を詰めて並べる。常設の表示状態文は削除する。32pxの操作高を揃え、フェードの単位は入力欄の内側に置く。画像なしでは黒を使用する既存動作は維持する。

## キューの解像度表示

映像出力先のWindowsデバイスキーは変更せず、画面表示のみディスプレイ番号・解像度・メイン区分へ整形する。未接続の保存済み出力先も同じ整形を用いる。ネイティブのOutputDisplay構造体にフィールドを追加せずABIを維持する。

手動停止では音声出力が有効かつ内部状態がWaitingになった場合、映像と同じ「停止中」と表示する。同期所有中の待機、音声OFF・準備中・再同期中・音声なし・エラーの表示は維持し、音声エンジンの状態や停止処理を変更しない。



### HAP_PREPARATION.md

[現在の資料](../../development/implementation/HAP_PREPARATION.md)

# HAPの準備と解析結果の再利用

進捗通知はUIへ渡す際にも要求・準備操作の同一性、取消、LTC権限を確認する。完了済み・取消済みの通知でIsBusyを復活させない。行選択では準備しない。出力ON/OFFもキャッシュ・キャンバス・同期・音声を作り直す理由にしない。

[MediaAnalysisService.cs](../../../src/LtcPlayer.App/MediaAnalysisService.cs)・[HapIndexCache.cs](../../../src/LtcPlayer.App/HapIndexCache.cs)・[MovHapIndex.cs](../../../src/LtcPlayer.App/MovHapIndex.cs)・[MovAtomReader.cs](../../../src/LtcPlayer.App/MovAtomReader.cs)

[技術解説の索引](../../development/implementation/README.md)・[整理時の旧記述](2026-11-20_007_TECHNICAL_REFERENCE_CLEANUP.md)



### LTC_SETTINGS.md

[現在の資料](../../development/implementation/LTC_SETTINGS.md)

外部映像出力の接続と、LTCによる再生位置の所有権を分ける。設定の違いに応じて必要な資源だけを更新する。

| 設定 | 反映 | 維持するもの |
|---|---|---|
| 補間時間・再同期閾値 | 次に実際に受けたLTCフレームから使用 | 時計、入力、同期実行、動画、音声、GPU、出力接続 |
| 補正フレーム数 | 手動には影響なし。受信中は新しい目標へ再同期。LOST保持は次の実受信まで維持 | 入力、時計、音声出力機器、GPU、蓋絵、索引キャッシュ |
| LTC入力機器・チャンネル・形式 | 同期素材を黒・無音にし、旧入力終了→新入力開始確認→保存→確定 | 外部出力、GPU、蓋絵、音声出力機器。手動素材は維持 |
| 映像出力先・方式・キャンバス | 外部出力停止後に変更 | 入力・時計など、出力接続とは別の資源 |
| 音声出力機器 | 音声OFF後に変更 | 映像の再生・外部出力 |

`--verify-ltc-settings`で時計の境界状態、手動・同期・LOST、資源の維持、補正後の音声位置、入力開始待ち・切替失敗・保存失敗・復旧失敗・古い通知・終了取消を検証する。入力切替の成功・失敗は検証用入力で再現するため、実際の機器・ケーブルを切り替えた物理検証とは区別する。

[MasterClock.cs](../../../src/LtcPlayer.App/MasterClock.cs)・[LtcMonitorService.cs](../../../src/LtcPlayer.Avalonia/LtcMonitorService.cs)・[PlaybackCoordinator.cs](../../../src/LtcPlayer.Avalonia/Playback/PlaybackCoordinator.cs)

[技術解説の索引](../../development/implementation/README.md)・[整理時の旧記述](2026-11-20_007_TECHNICAL_REFERENCE_CLEANUP.md)



### OPERATION_ACCESS.md

[現在の資料](../../development/implementation/OPERATION_ACCESS.md)

# 操作と再生所有権

行の選択、手動再生、LTC同期、外部映像出力は、それぞれ別の状態として管理する。行選択だけでは素材を準備しない。操作の可否は、現在のLTC状態、素材準備、編集・設定処理、出力切替、終了状態から判断する。

| 操作 | 条件 |
|---|---|
| 行選択 | 素材準備や表示変更をしない |
| 行の▶ | LTCが手動操作を許す状態で、素材準備・終了・出力切替中でない。キューの有効チェックやTCエラーは使わない |
| 停止・シーク | 上記に加え、表示中の手動素材があり、同期が表示面を保持していない |
| 保存 | 1件以上あり、LTC受信・再同期・補間中でなく、終了・編集適用・他の保存中でない。外部出力・手動再生・LOST保持・入力エラーは条件にしない |
| 構成編集・読込 | LTCが未受信（WAITING/LOST）なら許可。同期の保持画像や出力接続は条件にしない |
| 表示順変更 | 同期中も許可。出力切替・編集反映・終了中は不可 |

素材の準備は行の▶など明示的な再生操作で行う。選択状態と、実際に読み込んだ素材・再生セッションは独立している。


## 未受信中の構成編集

LOSTで補間が終了したら、同期映像を保持していても構成編集を許可する。編集権限と手動映像の操作権限を区別し、保持映像へのシークは禁止したままにする。LTC復帰時には未確定TCを取消す。

設定ごとの適用時点、入力切替時の保存・復旧、資源の寿命は[LTC設定の個別適用](../../development/implementation/LTC_SETTINGS.md)を参照。

[MainWindow.OperationAccess.cs](../../../src/LtcPlayer.Avalonia/MainWindow.OperationAccess.cs)・[PlaybackCoordinator.cs](../../../src/LtcPlayer.Avalonia/Playback/PlaybackCoordinator.cs)・[ShowEditor.cs](../../../src/LtcPlayer.Avalonia/ShowEditor.cs)・[MainWindow.Timecodes.cs](../../../src/LtcPlayer.Avalonia/MainWindow.Timecodes.cs)・[MainWindow.CoverChanges.cs](../../../src/LtcPlayer.Avalonia/MainWindow.CoverChanges.cs)

[技術解説の索引](../../development/implementation/README.md)・[整理時の旧記述](2026-11-20_007_TECHNICAL_REFERENCE_CLEANUP.md)



### PLAYBACK_ERRORS.md

[現在の資料](../../development/implementation/PLAYBACK_ERRORS.md)

# 素材エラーと再生状態の管理

## 読み込み・保存

空／重複IDは一時リストの段階で修復し、通常のTC変更・並べ替えではIDを変更しない。修復したJSONは自動上書きせず次の通常保存で反映。素材エラーやTC重複を含むリストも保存可能。壊れたJSON・未対応形式・件数上限は読込を拒否し、元のリストを維持する。


保存は表示用の検証や素材確認を実行せず、一覧のDTOを作る。再生中にも保存でき、保存取消・書込失敗では既存ファイルを保持する。行選択自体は素材再確認の契機にせず、明示的な行の▶または素材監視によって確認する。操作権限の詳細は[操作と再生所有権](../../development/implementation/OPERATION_ACCESS.md)を参照。

[PlaybackTimeline.cs](../../../src/LtcPlayer.App/PlaybackTimeline.cs)・[MaterialMonitor.cs](../../../src/LtcPlayer.Avalonia/MaterialMonitor.cs)・[ProgramPlayback.cs](../../../src/LtcPlayer.Avalonia/Playback/ProgramPlayback.cs)

[技術解説の索引](../../development/implementation/README.md)・[整理時の旧記述](2026-11-20_007_TECHNICAL_REFERENCE_CLEANUP.md)



### SYNC_RECOVERY.md

[現在の資料](../../development/implementation/SYNC_RECOVERY.md)

# 同期遅延の検出と復旧

## 動作

- 遅れが200msまたは動画の3フレーム分の大きい方を超えると、遅延秒数を画面に表示します。
- 現在の復号を維持しながら、LTCの現在位置より少し先を別の復号処理で準備します。
- 準備ができたフレームは、そのタイムコードに達してから採用します。
- 復号が遅い間に同じ準備処理を何度も破棄し直すことはしません。復旧の試行開始は最短でも1秒間隔です。復旧用の準備に失敗した場合は、次の試行まで2秒待ちます。
- 先読みする時間は、観測した初期準備時間を基に250ms～2秒の範囲で決めます。
- 復旧中は次キューの先読み用リソースを使い、現在の復号と合わせて同時に2つまでに抑えます。
- LTC入力が失われた場合は最後の映像を保持します。入力復帰やジャンプで世代が変わった場合は、古い復旧処理も破棄します。提示直前・観測値の採用時にも時計の世代と再生権限を確認し、待機中に届いた新しいLTCを優先します。
- 復号そのものが失敗した場合は、そのキューを同期対象から除外し、黒・無音で次の正常キューを待つ素材エラー処理へ伝えます。

`dotnet run --project tests/SyncRecovery/SyncRecovery.csproj -c Release`

実際の時計・キュースケジューラー・フレームバッファを使い、復号と描画だけを代替した回帰試験です。連続入力、低速復号、遅い準備、負荷からの回復、前後へのジャンプ、入力喪失と復帰、復号エラーを検証します。

実動画・GPUの試験は、検証用ビルドの `--verify-sync-recovery-output` で行います。30秒のHAP素材を使い、連続入力・入力喪失時の保持・入力復帰・前後へのジャンプからの復旧を確認します。通常の `scripts/test.ps1` に両方を組み込んでいます。

連続入力とジャンプは、それぞれ独立した試験条件として検証します。

ジャンプ先を開く際は、目標位置から動画の1フレーム分だけ巻き戻して準備する。準備後に通常の復号が追いつかない場合は、上記の遅延検出と復旧処理を適用する。

古い処理や先読みの終了を待った後は最新の時計から位置と対象キューを再評価する。待機中に世代・権限が変わった要求は採用しない。初回受信や入力エラーは世代番号だけでは区別できないため、LTC優先／手動許可／入力エラーの区分も比較する。採用されたフレームだけが保持画像のID・PTS・提示回数を更新する。先読みの引継ぎにはキューIDだけでなく素材版を含む定義の一致を要求する。

`tests/JumpRecovery`では提示待ち中の前後ジャンプ、終了待ち中の再ジャンプ・位置進行、初回受信、入力エラー、準備が短い／遅い場合の復旧、先読み素材版の相違を検証する。

[ProgramPlayback.cs](../../../src/LtcPlayer.Avalonia/Playback/ProgramPlayback.cs)・[Program.cs](../../../tests/SyncRecovery/Program.cs)・[Program.cs](../../../tests/JumpRecovery/Program.cs)

[技術解説の索引](../../development/implementation/README.md)・[整理時の旧記述](2026-11-20_007_TECHNICAL_REFERENCE_CLEANUP.md)



### THEMING.md

[現在の資料](../../maintenance/THEMING.md)

| 基本色 | 用途 |
|---|---|
| AppBackground | アプリ全体の背景 |
| PanelBackground | パネル背景 |
| Border | 境界線・出力待機ボタン |
| Accent | アクセント・選択状態・情報文字の基準 |
| SecondaryText | 補助文字・説明文 |
| IconFallback | 文字色を継承できない場合のアイコン色 |
| Warning | 警告・未適用の変更 |
| Success | 正常・適用成功 |
| OutputActive | 外部出力中のボタン |
| PlayingCue | 再生中のキュー背景 |

## 自動生成する色

- ホバーと押下：Accentを白に30%・55%近づけます。
- 控えめなアクセント：AccentをPanelBackgroundに25%近づけます。
- 選択背景：PanelBackgroundを白に16%近づけます。ホバー23%、非フォーカス10%、非フォーカスでのホバー16%です。
- 音声ONの背景：PanelBackgroundをAccentに18%近づけます。
- プレビュー周囲：PanelBackgroundとBorderの中間色です。
- 情報・ステータス文字はAccentHover、説明文はSecondaryText、出力待機ボタンはBorderを共用します。

混色は不透明なRGB値を生成します。基本色を変更すると関連色も追従しますが、明るい配色に大きく変更する場合は文字や選択状態の見やすさを確認してください。基本色は不透明な6桁の色コードで指定します。

[技術解説の索引](../../development/implementation/README.md)・[整理時の旧記述](2026-11-20_007_TECHNICAL_REFERENCE_CLEANUP.md)

## 確認結果

リポジトリ内Markdownリンク・画像パスと現行仕様の節リンクを点検し、リンク切れなし。旧referenceの7資料に移動先があり、旧フォルダーは空であることを確認して削除した。配布スクリプトの固定資料14件と対象版の確認記録の存在、JSON読込、git diff --checkを確認。アプリ・試験コードは変更せず、実行試験や配布物の作成は行っていない。

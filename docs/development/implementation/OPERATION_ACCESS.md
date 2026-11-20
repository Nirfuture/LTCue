# 操作の所有権と確定境界

## この資料について

この資料では、手動再生とLTC同期が切り替わるときや、保存・編集の待機中に状態が変わるときに、各処理が何を続け、どの結果を採用するかを説明します。操作の競合や、取消・反映のタイミングを確認するときに参照してください。

操作の許可条件は、[現行仕様のキュー編集](../FINAL_SPEC.md#3-キュー編集と選択)・[LTCと手動再生](../FINAL_SPEC.md#4-ltcと手動再生)・[保存](../FINAL_SPEC.md#8-設定保存障害)を参照してください。この資料では、実装がその条件を非同期処理の途中でも守るための責務と確認箇所を扱います。

## 全体の関係

MainWindowとPlaybackCoordinatorは、行の選択、実際に準備した素材、同期による表示面の所有権、外部出力の接続を別々に管理します。MainWindowはボタンの有効状態を決める際に権限を確認し、再生・編集の処理側も結果を反映する直前に現在の権限を確認します。

- `MainWindow.outputSessionActive`：外部映像出力の開始から停止までの寿命。映像出力先・方式・キャンバス変更の制限に使う。
- `PlaybackCoordinator.ManualAvailable` / `LtcHasPriority`：共通LTC時計からその場で導く再生の優先権。
- `PlaybackCoordinator.OwnsSurface` / `PreviewPlayer.IsSyncOwned`：同期セッションが表示面を所有しているか。LOSTでも保持画像は手動素材として扱わない。
- `MainWindow.previewCue` / `PreviewPlayer.LoadedPath`：実際に準備・表示した手動素材。DataGridの選択とは別。

UIの可否は `MainWindow.OperationAccess.cs` に集め、ボタン・シーク操作の入口とツールチップで同じ判定を使う。判定結果を別の権限フラグとして保持しない。

## 通常の操作と確定

### 選択集合と変更の公開

複数選択はDataGridのExtended選択に任せ、操作時に存在するCueIdの集合を取得する。再生コマンドは押した行を明示し、選択を書き換えない。一括削除は表示元を含む場合だけ素材解放を一度行い、権限を再確認してからShowEditor.DeleteManyで全件変更後に一度だけ検証する。複製は単独選択時に限定し、ハンドル操作では1行だけを移動して選択ID集合を復元する。選択モデルはDataGridに集約し、選択操作では素材を準備しない。

### 構成編集とTC入力

TCのOneWayバインディングと一つの編集セッションにより、入力途中の値はCueRowに書かない。4欄の間の移動は同じセッションを維持し、Enter／外へのフォーカス移動でまとめて確定、Esc／LTC取得で取消す。確定直前にもライブ権限を再確認する。ShowEditor.SetTimecodeはプロパティ通知に伴う個別検証を抑制し、最後に一度だけ同期定義を公開する。

TC欄と有効チェックはDataGrid.IsReadOnlyから有効・フォーカス状態を導く。disabledスタイルでTC欄を灰色にし、無効時にもツールチップを表示する。行選択とドラッグハンドルは無効化しない。

構成操作はShowEditorのライブ権限コールバックでも拒否する。表示順変更には別の権限を使う。Importはリスト外で素材準備し、最後に権限を確認して反映する。Loadも準備完了後に権限を再確認する。編集の準備状態は設定処理のbusyと分け、LTCの受信・同期開始を妨げない。設定適用は編集準備との競合を拒否する。

表示元削除とリスト置換はPreviewPlayer.ClearAsyncで素材を解除する。停止して先頭フレームへ戻すStopAsyncとは別の責務。Clearも取消・コマンド直列化・同期優先の再確認を行い、表示元削除時に外部接続や蓋絵を解除しない。

### 開始TCの一括編集

CueTimecodesはCSV・テキストの解析と、LTCフレーム単位での配置計算を担当し、ライブの行を変更しない。CueTimecodePlanは対象ID・元のTC・素材版・必要な長さと、新しいTCの読み取り専用リストを持つ。ShowEditor.SetTimecodesは現在の編集権限、形式、対象の存在・変更を確認してから全行を変更し、一度だけ検証する。単独TC入力も同じ反映処理を使い、不正TCはステータスとして残す。

MainWindow.Timecodesは対象選択とモーダル画面の寿命を持つ。一覧順の変更も反映直前に確認する。CSV読み込みはサイズ制限と終了時の取消を持ち、LTC受信は妨げない。追加方式の保存はPendingSettingsで確定してからライブ設定へ反映し、映像・音声・入力の再設定は行わない。

ShowEditor.ImportAsyncは一覧外で素材を準備する。自動配置時は追加直前の一覧から最も遅い終了時刻を求め、追加する全行の開始TCを計算してから反映する。計算失敗やLTC取得で一部だけ追加しない。複製と通常の0時追加は自動配置とは別に扱う。

### 保存

MainWindowは保存の寿命・ファイル選択・二重実行・終了時の取消を持つ。SaveContentAccessが内容と現在LTCによる可否を計算し、SaveAccessは保存操作中の二重実行拒否を加える。内容の条件はファイル選択後にも確認し、所有中の保存を自分自身で拒否しない。書込処理開始後のLTC取得では保存を取り消さない。ShowEditorはUIスレッドで一覧を独立したShowProgramDocumentにコピーする。ShowProgramServiceはそのDTOをJSON化し、保存先と同一フォルダーの一意な一時ファイルへ非同期で書き、完了後に置換する。取消・失敗時は一時ファイルを削除する。置換完了後の取消は完了済み保存を巻き戻さない。

保存はEditorの検証・素材解析・PlaybackChangedを起こさず、再生セッション、表示面、WASAPI出力、先読みを作り直さない。書込中の編集は保存用DTOに影響しない。終了処理は保存の取消と後始末を待ってから再生系を破棄する。

### 蓋絵の差替え

MainWindow.CoverChangesは画像選択・取消・終了待ちを独立して管理する。蓋絵の変更はLTCによる再生位置の所有権と無関係に許可する。別の設定反映・画像準備・終了・共通GPU障害中は拒否する。設定適用側も画像準備との競合を拒否する。

CPUでの画像読込後、RenderSession.PreparedCoverでGPUテクスチャを作成する。この段階では現在画像を変更しない。PendingSettingsの保存を成功させた後、awaitを挟まず、確保済みGPU資源・CPU画像・設定を確定する。GPUの確定操作は新たな割当を行わない。失敗・取消で未確定資源を破棄しても、元の画像は残る。

PreviewHostのCoverBindingへ確定済み画像を伝え、次のフレームで同じ画像を再アップロードしない。表示量・遷移開始時刻・目標は変更せず、保持画像でも再合成して反映する。保存する設定は画像準備完了時点のものを基にし、準備中の音量・フェード変更を戻さない。

設定適用経路でも同じGPU準備・確定を利用する。キャンバスを変更する場合は新しいキャンバス上で画像を準備する。共通GPU障害は画像単独の準備失敗と区別し、再生系の停止処理へ伝える。画像のGPU転送・設定保存には実行負荷があり、物理的な完全無瞬断を保証するものではない。

## 失敗・取消・競合

### 非同期処理の競合

UI判定の後でもLTC状態は変わるため、PreviewPlayerもCoordinatorのライブ権限判定を参照する。Load/Play/Seekは待機や準備の後、提示・再開の直前に権限・要求世代・取消を確認する。手動再生ループもLTC優先への移行を検出する。同期開始は手動の準備を取り消し、同じコマンド直列化を通る。同期と手動の別キャンバスや重複した時計は作らない。

### 保持元と編集予定の分離

ProgramPlaybackはLOST/ERRORの保持中、編集後のスケジュールを表示元として再選択せず、実際に開いていたactive素材を観測に使う。表示名・素材ID・保持位置・画素を維持する。予定は更新され、LTC復帰後に再評価する。PlaybackTimelineは開始位置・長さが変わった実行区間を引き継がない。

保持元の削除とリスト置換にはCoordinator.ClearHeldSourceAsyncを使う。同期開始と同じコマンド直列化の中で、未受信・期待する保持元を確認し、古い同期実行を終了して素材を解除する。待機後のLTC状態も確認し、受信が復帰したらクリアと削除を中止する。別キューの削除では同期の保持状態を解除しない。外部出力・蓋絵は維持する。

## 実装の参照先

[MainWindow.OperationAccess.cs](../../../src/LtcPlayer.Avalonia/MainWindow.OperationAccess.cs)・[PlaybackCoordinator.cs](../../../src/LtcPlayer.Avalonia/Playback/PlaybackCoordinator.cs)・[ShowEditor.cs](../../../src/LtcPlayer.Avalonia/ShowEditor.cs)・[MainWindow.Timecodes.cs](../../../src/LtcPlayer.Avalonia/MainWindow.Timecodes.cs)・[MainWindow.CoverChanges.cs](../../../src/LtcPlayer.Avalonia/MainWindow.CoverChanges.cs)

[実装補足の索引](README.md)・[今回の整理記録](../../history/records/2026-11-20_009_SPEC_IMPLEMENTATION_REORGANIZATION.md)

# 次セッションへの指示：キュー選択で上部操作ボタンが隠れる問題の修正

## 目的と依頼

高さの低いウィンドウで、表示済みのキューをクリックしただけで中央領域が自動スクロールし、キュー追加・削除・複製・保存・読込ボタンが隠れる問題を修正する。

この指示書を作成したセッションでは通常版で再現確認とコード調査を行った。製品コードの修正は未着手。次セッションは原因の確定、最小修正、回帰検証、通常版ビルドまで行う。commit・push・merge・releaseは、次セッションでユーザーから指示がなければ行わない。

## 作業場所と開始状態

- リポジトリ：`C:/Users/izumi/Desktop/LTCue/LTCue`（ワークスペース直下ではなく、その1階層下）
- ブランチ：`perf/mp4-single-decode-start-20261120000643`
- 基準HEAD：`b8f3be9f87488b01c24e7aafde280fb96dd6fdc0`
- 指示書作成直前の作業ツリーはクリーン。本書は新規の未コミット文書。
- 開始時にstatus・HEAD・ブランチ・適用されるAGENTS.mdを再確認し、後から加わった変更を保持する。
- 現在のチェックアウトを利用し、reset・clean・無差別なstashをしない。既存の通常版の上書きや、ユーザーが起動したアプリの終了はしない。

前セッションで変更は4コミットに整理済み：素材情報共有・PCM/MP4起動改善と検証、計測スクリプト、4分素材、関連資料。本件では再生処理を変更しない。

## 確認済みの再現

確認した通常版：

`artifacts/mp4-single-decode-start-20261120-000640/product/LTCue.exe`

1. 同梱のHAPキュー4本がある状態で起動。外部出力・再生は開始しない。
2. 横幅約1640、高さ約1100では、ファイル名セルや開始TC欄をクリックしても上部ボタンは隠れなかった。
3. 幅を保ってウィンドウ高さを約650に縮める。中央領域の先頭には操作ボタンと一覧ヘッダーが見えている。
4. 2行目のファイル名セルを通常クリックする。
5. 中央領域全体が約64px上へ移動し、操作ボタンが隠れ、一覧ヘッダーが中央領域の上端に来る。右側のプレビュー領域も同時に移動する。
6. 最上部のLTC表示・設定・出力開始ボタンは固定のまま。

約64pxは画面観察による概数。ScrollViewer.Offsetを採取した実測値ではない。スクリーンショットはツール表示で確認したが、リポジトリへ画像保存はしていない。修正前後の証跡は次セッションで新たに取得する。

手動ホイールで中央領域がスクロールすること自体は既存仕様。今回の不具合は、既に見えているキューを選ぶだけで表示位置が跳ぶこと。

## 読むファイル

- `docs/FINAL_SPEC.md`：レスポンシブ配置、高さ不足時のスクロール、初期高さ調整
- `docs/BUILD.md`、`docs/VERIFICATION.md`
- `src/LtcPlayer.Avalonia/MainWindow.axaml`：`WorkspaceScroll`、`Workspace`、`EditorTools`、`CueListViewport`、`CueGrid`
- `src/LtcPlayer.Avalonia/MainWindow.Responsive.cs`：`UpdateResponsiveLayout`、`FitInitialContentHeight`
- `src/LtcPlayer.Avalonia/MainWindow.axaml.cs`：一覧選択イベント、初期化
- `src/LtcPlayer.Avalonia/MainWindow.CueEditing.cs`、`MainWindow.FileLocation.cs`：セルフォーカス・クリック
- `src/LtcPlayer.Avalonia/ResizeAnchorVerification.cs`
- `src/LtcPlayer.Avalonia/CoverHeightVerification.cs`
- `src/LtcPlayer.Avalonia/ResponsiveVerification.cs`
- `src/LtcPlayer.Avalonia/MultiSelectionVerification.cs`
- `src/LtcPlayer.Avalonia/TimecodeEditingVerification.cs`（実在する対象ファイル・スイート名は探索して確認）
- `scripts/verification-suites.json` と検証ディスパッチ実装

## 原因の仮説と確認方法

`WorkspaceScroll`が編集ペイン全体と右側ペインを包み、その内側にDataGrid固有のスクロール領域がある。選択/フォーカスに伴うBringIntoView要求が、外側にまで作用している可能性が高い。現時点では発生源とイベント経路を計測しておらず、確定原因として扱わない。

使用中のAvaloniaは11.3.21。ローカルNuGet XMLドキュメントには、`ScrollViewer.BringIntoViewOnFocusChange`は既定trueで、フォーカス時の自動表示スクロールを制御するとある。確認した資料：

`C:/Users/izumi/.nuget/packages/avalonia/11.3.21/lib/net8.0/Avalonia.Controls.xml`

まず修正前の再現試験を作り、`WorkspaceScroll.Offset`、一覧内ScrollViewerのOffset、選択行ID、フォーカス先、操作ボタンと一覧ヘッダーのウィンドウ座標を前後で記録する。必要なら検証限定で`RequestBringIntoView`のSource/TargetObject/TargetRectと伝播順を記録する。実際のAPI形状は参照アセンブリ等で確認する。診断ログは製品へ常時追加しない。

## 採用する修正方針

**一覧を選択する際の不要な外側自動スクロールを止める。画面構造や固定ヘッダーの設計変更は行わない。**

1. 第一候補は外側`WorkspaceScroll`の`BringIntoViewOnFocusChange="False"`。外側だけに設定し、DataGridやWindow全体に広く設定しない。これで再現が止まるか、内側のキーボード追従や画面外コントロールへのTab移動を損なわないかを検証する。
2. 明示的なBringIntoView要求が原因で設定だけでは止まらない、または外側のフォーカス追従が必要な箇所を壊す場合、イベント経路を確定して限定的な対策へ切り替える。DataGrid内側の必要なスクロールを完了させた後、不要な要求が外側へ伝播する境界でのみ抑える。現在の表示範囲・要求元・操作種別を根拠に対象を限定する。
3. 既に可視な行への通常クリックでは外側Offsetを変えない。選択・複数選択・TC編集そのものは継続する。
4. 画面外の行へのキーボード移動ではDataGrid内側の自動スクロールを維持する。外側にも必要な移動がある場合は、対象が到達可能になる最小限にし、仕様と試験へ明記する。
5. 最小の設定変更で十分なら、それ以上のイベント処理を加えない。限定策でも実現が困難な場合は原因とトレードオフを整理し、画面構造の大幅変更前にユーザーへ相談する。

避ける対策：選択のたびにOffset=0へ戻す、DispatcherでOffsetを戻してちらつきを隠す、ホイール操作を無効にする、全BringIntoView要求を一律握りつぶす、フォーカスを外して選択やキーボード操作を壊す、最小ウィンドウ高さを増やす、再生・同期・音声処理を変更する。

## 合格条件と回帰試験

修正前に失敗し修正後に成功する対象回帰試験を追加する。`SelectedItem`の代入だけで済ませず、実際のセルへのクリックとフォーカス遷移を通す。既存MultiSelectionVerificationの入力合成を参考にし、最後は通常版の実UIでも確認する。

| 条件 | 確認する結果 |
|---|---|
| 横幅1640前後・高さ650前後、4行、外側Offset=0、別行ファイル名をクリック | 外側Offsetと操作ボタンの位置を維持し、選択は変更される |
| 高さ500前後・940前後、1行と4行、同一行再クリック | 表示済みセルへのクリックで不要な外側移動がない |
| 外側を手動で少しスクロール済み | 表示済み行のクリックで先頭へ強制復帰せず、現在位置を維持する |
| 開始TC欄、チェックボックス、Ctrl/Shift複数選択 | フォーカス・編集・選択集合が正常。不要な外側移動がない |
| 多数行、上下キー・PageUp/PageDown・Home/End等の対応済み操作 | 画面外の選択行を内側スクロールで表示できる |
| 狭幅700前後、縦積み、高さ不足 | ホイール/スクロールバーでプレビュー・音声・蓋絵まで到達可能 |
| Tab/Shift+Tabで画面外の操作へ移動 | フォーカス先が確認・操作可能。必要な外側フォーカス追従を失わない |
| リサイズ、右側プレビューの縦横比、警告表示の変化 | 既存の高さ調整・一覧下端・固定上部表示の挙動を維持 |

位置判定は0.5〜1 DIP程度の丸め誤差を考慮し、固定sleepだけで完了を推測せずレイアウト安定/期待状態を待つ。新試験のディスパッチ・カタログ件数・証跡形式・タイムアウトを一致させる。既存試験の期待値を緩和して合格させない。

対象試験に加え、少なくともresize-anchor、cover-height、responsive、multi-selectionとTC編集関連を実行する。正確な現行スイート名をカタログで確認する。必要な既存チェックを終え、通常版と検証版を別の新規artifacts配下にビルドする。UI全般に影響する場合は正式一括も実行し、未実施のチェックを明記する。

前セッションの簡易ビルドではAvaloniaテレメトリーが作業領域外へ書き込もうとして失敗し、`-p:UsedAvaloniaProducts=`でテレメトリー対象を空にした検証ビルドは警告0・エラー0で成功した。環境制限が再発した場合の参考であり、ソースに設定変更を追加する必要はない。`artifacts/commit-check-20261120`は検証用で、配布用通常版ではない。

## 完了報告

- 確定した発生源と、外側だけを抑える修正の理由
- 変更ファイル、修正前後のOffset/座標と画面証跡
- 対象回帰・既存回帰の結果、未確認事項
- 新しい通常版EXEの絶対パス（依存DLL等を含むフォルダーも必要）
- git statusと、製品コード/試験/資料の変更範囲

ユーザーは別セッションへ移るため、既存会話を読まなくても本書だけで再現・修正・検証・報告まで進めること。

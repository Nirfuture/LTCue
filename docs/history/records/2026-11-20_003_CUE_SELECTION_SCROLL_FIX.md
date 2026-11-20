# キュー選択時の外側スクロール修正（2026-11-20）

作業開始時のHEADはb8f3be9f87488b01c24e7aafde280fb96dd6fdc0、ブランチはperf/mp4-single-decode-start-20261120000643。開始時の未コミット変更は修正指示書のみで、保持した。

## 発生源と修正

通常クリックによりDataGrid自身へフォーカスが移る。Avalonia 11.3.21のScrollViewer.OnGotFocusは子のControl.BringIntoViewを呼ぶため、外側WorkspaceScrollが高さの大きい一覧全体を表示しようとして移動する。

参照：[Avalonia 11.3.21 ScrollViewer.OnGotFocus](https://github.com/AvaloniaUI/Avalonia/blob/11.3.21/src/Avalonia.Controls/ScrollViewer.cs#L699)。ローカルパッケージのXMLも参照した。

第一候補のBringIntoViewOnFocusChange=Falseではクリックの問題は止まるが、狭幅画面で画面外の蓋絵変更ボタンへのTabフォーカス追従も止まった。この設定は採用しなかった。

WorkspaceScrollViewerで、DataGrid自身がPointer/Unspecifiedでフォーカスを得た場合だけ外側のOnGotFocusによるBringIntoView発行を省く。DataGridのクリックはUnspecifiedでフォーカスを設定するため、Pointerだけの判定では再現が残る。Tab/DirectionalとTCなど子コントロールは標準処理を通す。明示的なBringIntoViewイベントは抑えず、一覧内の行表示処理や手動スクロールも変更しない。標準ScrollViewerのStyleKeyを維持する。

## 修正前後の測定

証跡の起点は`artifacts/cue-selection-scroll-20261120/`。単位はDIP。

| 操作 | 修正前の外側Offset Y | 修正後の外側Offset Y | 修正前の操作ボタンY | 修正後の操作ボタンY |
|---|---|---|---|---|
| 幅1640・高さ650・4行クリック | 0→64 | 0→0 | 124.67→60.67 | 124.67→124.67 |
| 幅1640・高さ500・1行クリック | 0→64 | 0→0 | 124.67→60.67 | 124.67→124.67 |
| 外側24まで手動移動後にクリック | 24→64 | 24→24 | 100.67→60.67 | 100.67→100.67 |

最終の同一試験による修正前：`baseline-final-results/measurements.json`と`file-650-4-before.png`/`file-650-4-after.png`。修正後：`verified-final-results/`に同形式で保存。修正前にはDataGridCellに続いてDataGrid自身をTargetとするBringIntoView要求を記録し、修正後にはセルの要求を保ちつつ不要なDataGrid自身の要求が消えることを確認した。最初の2回の起動はFFmpegへの環境制限で失敗し、原因を記録して権限のある実行環境で再測定した。

初期の回帰試験では一覧内スクロールの測定対象にTCテキストボックス内部のScrollViewerを選んでいたため、その判定は使用しない。DataGrid自身をTemplatedParentとする縦スクロールバー値を測る方式へ修正した。これは製品側の修正ではない。

## 検証

対象回帰13項目は合格。製品版と検証版を別々の新規フォルダーへ、ランタイムを同梱してpublishした。再生・同期・音声処理の製品コードは変更していない。

- 正式一括：`acceptance-v2/summary.json`、59スイート623項目すべてPASS（計算系4スイートを含む）。対象回帰13、resize-anchor 40、cover-height 8、responsive 8、multi-selection 22、cue-editing 12、timecode-editing 10、TC計算32を含む。
- 最終の同一対象試験：`baseline-final-results/results.json`は13項目中6件FAIL、`verified-final-results/results.json`は13項目すべてPASS。Down/Up/PageDown/PageUp/Ctrl+End/Ctrl+HomeとDataGrid自身の縦スクロールバー値を確認した。
- 一括検証のバイナリは`verification/`。その後は検証限定の記録を補強し、キー操作の確認を追加、起動失敗時の新スイートの証跡保存先を追加した。`verification-final-v2/`で対象13項目を再実行した。製品のスクロール修正は一括検証時から変更していない。
- 一括試験の最初の起動はAudioFixturesPathの指定誤りで試験開始前に停止した。正しい共通fixturesを指定した`acceptance-v2/`で全件を完走した。
- 通常版実UI：ユーザーが旧版を終了した後、`product-final/LTCue.exe`を独立設定で起動。4本のHAPキュー、幅約1640、高さ約650で1行目→2行目を実クリックし、操作ボタンとヘッダーが動かないことを画面で確認した。ホイールで中央領域を移動でき、移動済み位置での選択は位置を維持し、Shift+Tabで読込ボタンへ戻れることも確認した。外部出力・再生は開始していない。実UI画面はツール表示で確認し、ファイル保存した画像証跡は検証版のPNGを使用する。
- 実LTC、物理AV同期、別機器、長時間耐久は今回の対象外。全キー組合せとすべてのTab順序を網羅した試験ではない。

## ビルド

最終通常版：`C:/Users/izumi/Desktop/LTCue/LTCue/artifacts/cue-selection-scroll-20261120/product-final/LTCue.exe`。同フォルダーのDLL・TestMedia等を含めて使用する。実UI確認後、この通常版を開いた状態で残した。SHA-256は`final-binary-manifest.json`に記録した。

通常版ビルドはEnableVerification=false、検証版はtrue、両方UsedAvaloniaProductsを空にしてpublishした。ネイティブDLLは既存ビルドを同梱し、今回の変更対象ではない。既存の通常版フォルダーは上書きしていない。

commit・push・merge・releaseは行っていない。

## 変更範囲と終了状態

製品：`MainWindow.axaml`の外側型変更と`WorkspaceScrollViewer.cs`の追加。試験：`CueSelectionScrollVerification.cs`、`MainWindow.axaml.cs`のディスパッチ/失敗証跡、`scripts/verification-suites.json`。資料：FINAL_SPEC、VERIFICATION、本記録。既存のIMPLEMENT指示書は保持した。

git statusは追跡済み5ファイル変更、新規4ファイル（うち1つは開始前からある指示書）。git diff --checkは成功。通常版/検証版の最終publishは成功し、コンパイル警告・エラーなし。

# キューファイルの場所を開く

キューの動画ファイル名を右クリックし、「エクスプローラーで場所を開く」を選べるようにした。

- 存在するファイルは、親フォルダーを開いてファイルを選択する。
- ファイルが見つからず、親フォルダーが残っている場合は、そのフォルダーを開く。
- 両方見つからない場合はステータス欄に知らせる。
- 対象は選択中のキューではなく、右クリックした行のファイル。右クリックによる別キューの選択・読み込みを抑制する。
- Windows Shell APIへパスを渡し、コマンド文字列を組み立てない。ファイルの確認とShell処理は別のSTAスレッドで行う。

ビルド警告0・エラー0、差分形式チェック通過。既存のキューUI5項目と共通再生13項目が通過した。初回のキューUI検証は旧PathIconだけを認める条件だったため、現行のLucideIconにも対応させて再実行した。Explorer実起動を自動検証したものではない。

証跡：`artifacts/redesign/file-location-cue-checked/results.json`、`artifacts/redesign/file-location-playback-integration/results.json`。

確認用ビルド：`C:\Users\r-izumi\Desktop\LTC_Player\artifacts\redesign\cue-file-location-win-x64\LtcPlayer.Avalonia.exe`

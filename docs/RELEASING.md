# リリース作成

## 1. バージョンと変更点を確定する

`src/LtcPlayer.Avalonia/LtcPlayer.Avalonia.csproj`のVersionと、`RELEASE_NOTES.md`を更新してコミットします。

配布物は指定したコミットからビルドします。作業中のファイルやIDEの設定は含めません。

## 未コミットの確認用候補

作業途中の動作確認用には、次を使用します。

```powershell
./scripts/package-release.ps1 -Version 0.1.0-preview.11 -WorkingTree
```

生成先は`artifacts/candidates/0.1.0-preview.11/`です。Git管理対象と未追跡のソースを保存し、無視対象の出力や個人設定は含めません。ソースZIPのSHA-256とファイルごとのハッシュを保存し、そのスナップショットからクリーンビルドします。

MANIFESTのSourceModeはWorkingTreeSnapshot、Commitはnull、BaseCommitは作業の基点です。未コミットの変更がそのコミットに含まれるとは表示しません。正式リリースを作る場合は、変更とバージョンをコミットし、以下のコミット指定手順で改めて作成します。同じ版の候補を作り直すときは`-CandidateId checked`などを指定し、別フォルダーへ保存します。既存の候補は上書きしません。

## 2. 配布ファイルを作る

ビルドに必要な環境を用意し、リポジトリのルートで実行します。

```powershell
./scripts/package-release.ps1 -Version 0.1.0-preview.11
```

通常のdotnetと異なる実行ファイルを使う場合は、`-DotnetPath`で指定できます。過去のコミットを指定する場合は`-Revision`を使います。ソース側のVersionと指定バージョンが一致しなければ停止します。

生成先:

```text
releases/0.1.0-preview.11/
├─ LTCue/                         展開状態のアプリ
├─ LTCue-0.1.0-preview.11-win-x64.zip
├─ LTCue-0.1.0-preview.11-source.zip
├─ SHA256SUMS.txt
├─ RELEASE_NOTES.md
├─ MANIFEST.json
└─ SIGNATURES.json
```

実行用ZIPにはLucide/Featherを含むライセンスとAudioNative.dllの対応ソースを同梱します。.NETランタイム同梱、検証用コードとローカル設定が入っていないことを作成時に確認します。完全なプロジェクトソースはsource.zipに保存します。MANIFEST.jsonにはバージョンとソースコミット、SIGNATURES.jsonには各EXE・DLLの署名状態を記録します。

既存のバージョンフォルダーは上書きしません。新しい配布には新しいバージョンを使います。失敗した作業は`artifacts/packaging/`に残り、最後まで成功した配布物だけを`releases/`へ移します。

## 3. 確認する

- `LTCue/LTCue.exe`を起動し、使用する機器で動作を確認する。
- 実行用ZIPを別フォルダーへ展開し、同じ操作ができることを確認する。
- MANIFEST.jsonのコミットが公開するソースと一致することを確認する。
- 動作確認の結果と未解決項目を確認する。

`releases/`と`artifacts/`はGitの管理対象外です。公開済みのフォルダーは残しておきます。

## 4. GitHub Releasesへアップロードする

MANIFEST.jsonに記録されたコミットへバージョンのタグを付け、GitHub Releasesを作成します。プレビュー版ではPre-releaseを選びます。

- 実行用ZIP
- source.zip
- SHA256SUMS.txt
- MANIFEST.json
- SIGNATURES.json

本文には同じフォルダーのRELEASE_NOTES.mdを使います。このスクリプトはGitHubへのアップロードや公開を行いません。

## 配布方針

当面は未署名で配布します。通常の作成コマンドで配布物と署名状態の記録を生成できます。

Smart App Controlによる起動ブロックの制約を、リリースノートに記載します。詳しくは[配布方針](DISTRIBUTION.md)を参照してください。

実行用ZIPの資料は`package-release.ps1`の明示リストに限定します。利用者向けREADMEは`docs/DISTRIBUTION_README.md`です。操作ガイドは`docs/USER_GUIDE.md`を同梱します。画像を挿入する際は、その画像も明示リストへ追加します。旧仕様・進捗・画像・開発資料はソースZIPにのみ収録します。

公開後は、リポジトリREADMEのWindows版ZIP直リンク・版番号・更新内容リンクを、実際に公開した版へ揃えます。未公開候補のリンクへ先に変更しません。

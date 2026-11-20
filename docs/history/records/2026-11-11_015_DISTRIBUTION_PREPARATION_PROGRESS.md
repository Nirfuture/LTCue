# 最終工程前・第4段階：仕様と配布候補

2026-11-11／0.1.0-preview.10／未公開・未署名の確認用候補

## 完了した整備

- FINAL_SPECを現行仕様として書き直し、共通キャンバス、独立した音声、設定の連続適用、未接続先、UI、描画障害の動作を一本化した。旧本文はdocs/historyへ保存した。
- QUICKSTART・COMPATIBILITY・TROUBLESHOOTINGを更新し、「プレビューだけ音声」「出力時に音量非表示」などの古い説明と古い画面案内を除去した。
- 旧要件・計画・開発指示には履歴資料であることと現行仕様への参照を明記した。
- Lucide/Featherのライセンス原文をLICENSES/Lucide.txtへ置き、通常ビルド・配布ZIPへ同梱。NOTICE、README、asset-licenses.jsonへ使用範囲を記載した。
- SDKを検証済みの10.0.203へ固定し、別SDKへ自動変更しない構成にした。アプリは0.1.0-preview.10とした。
- 未コミットの作業ツリー候補を明示して作れるよう配布スクリプトを整備した。ソースZIPと各ファイルのハッシュを保存し、そのソースからクリーンビルドする。

正式リリースのコミット指定ビルドも維持した。候補のMANIFESTではSourceMode=WorkingTreeSnapshot、Commit=null、BaseCommit=c4b60e1…とし、未コミットの内容を既存コミットの内容だとは示していない。Gitへのコミット、タグ作成、GitHubへの公開はしていない。

## 候補作成時に修正した問題

通常ビルドでもライセンスを同梱するようにした結果、配布処理が既存のLICENSESディレクトリと衝突した。内容を統合するコピーへ修正し、候補ZIPのライセンス原文一致を確認した。

候補の全検証では、ウィンドウが隠れた際のDXGI_STATUS_OCCLUDEDを旧描画APIが例外にしていたことと、試験のプレビュー表示回数が進まない条件を確認した。HRESULTが負の場合だけエラーとし、取消は従来どおり例外にする。検証版だけ画面を前面へ置き、画像比較・表示回数・復帰の確認を残して再実行した。正常・異常・取消の判定試験も追加した。製品版のウィンドウ配置は変更していない。

初回候補と失敗記録は残し、修正版は別の`0.1.0-preview.10-checked`フォルダーへ作成した。既存候補を上書きしていない。

## 最終結果

| 確認 | 結果 |
|---|---|
| SDK固定下での製品クリーンビルド | 成功 |
| ソースZIPからの検証版・CPU試験ビルド | 成功 |
| 配布ZIP内と同一のPlayerNative/AudioNativeを使った全自動検証 | 35スイートすべてPASS、結果・測定ケース293件 |
| ZIP/manifestのハッシュ照合、ソース各ファイルの照合 | PASS |
| ランタイム・ライセンス・対応ソースの同梱、原文一致 | PASS |
| 個人設定・bin/obj/build/.gitの除外 | PASS |
| 現行資料のリンク、版番号、検証コードの製品混入防止 | PASS |
| 同梱物チェック合計 | 8/8 PASS |
| git diff --check | PASS |

293件にはリサイズ、性能測定等を含む。293個の独立した単体テストの意味ではない。ランタイム同梱のため利用者による.NET Desktop Runtime・SDKの導入は不要。FFmpeg/ffprobeとVisual C++ x64ランタイムは外部前提。

ソースZIPのSHA-256：

```text
9dc0b1671fb7c7a5e746f4a1265b01c60d87df66e8e877bf6b805fc93a0edc90
```

実行用ZIP・対応ソース・MANIFEST・SHA256SUMS・署名状態は、ユーザー向けoutputs/preview.10-candidateへ保存した。VERIFICATION.json、package-checks.json、native-cohort.jsonは、候補作成後の検証記録として別添した。

リポジトリ内の候補：`artifacts/candidates/0.1.0-preview.10-checked/`。最終試験の証跡：`artifacts/candidate-qa-preview.10-checked/automatic/`。

## 未実施・次工程

展開した製品版の直接GUI起動は、Computer Useが`Computer Use app approval timed out`を返し、対象ウィンドウが生成されなかったため未実施。承認を迂回する操作は行っていない。展開ファイルの静的確認と、対応ソース・同一ネイティブDLLによる検証版の実行結果を、製品版の直接起動確認とは区別する。

実際のLTC源、機器の切断・再接続、他の環境、2時間以上の耐久、物理画面と出音の遅延、Smart App Control環境での起動は未確認。未署名ファイルを含み、署名済み配布とは扱わない。

次は同じ候補を使った実機・耐久確認。実機確認後に正式版を作る場合は、ソースをコミットして指定コミットから改めて配布物を作成し、候補と最終配布物を照合する。

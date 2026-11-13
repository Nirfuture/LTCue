# 資料・構成の案内

利用者向け：[クイックスタート](QUICKSTART.md)・[操作リファレンス](USER_GUIDE.md)・[対応環境](COMPATIBILITY.md)・[困ったとき](TROUBLESHOOTING.md)。

開発：[現行仕様](FINAL_SPEC.md)・[ビルド](BUILD.md)・[検証](VERIFICATION.md)・[リリース作成](RELEASING.md)。

| 場所 | 内容 |
|---|---|
| src/LtcPlayer.Avalonia | UI、再生、音声。Playbackは現行、Regressionは旧試験専用 |
| src/LtcPlayer.App | 本体へ取り込む共通時計・入力・素材処理。独立アプリではない |
| native | C++のLTC入力、WASAPI、GPU描画 |
| tests / scripts | 同期計算検証、ビルド・試験・配布手順。scripts/legacyは旧個別入口 |
| TestMedia | 初期デモにも使用するHAP動画4本とLTC音源 |
| LICENSES / third_party | 依存ライセンスとlibltcソース |
| docs/history | 過去の仕様・計画・進捗・レビュー |

## 現在の調査記録

- [検証環境Aの開始計測](STARTUP_CHECK_ENV_A.md)
- [検証環境Bとの比較](STARTUP_CHECK_ENV_B.md)
- [検証環境Aのプロセス起動診断](STARTUP_PROCESS_LAUNCH_ENV_A.md)
- [引き継ぎ時点の記録](HANDOFF_2026-11-13.md)（以後の結果は上記を参照）

[旧資料の案内](history/README.md)・[各版の確認記録](releases)。生成物、端末設定、生ログはGitへ追加しない。

配布用READMEの原稿はDISTRIBUTION_README.mdです。配布時にアプリ直下へ置くため、その相対リンクは配布後の位置を基準にしています。

## 過去の検証記録

検証記録・履歴資料に記載したコミットIDは、移行前の開発履歴を識別するものです。新リポジトリのコミットIDではありません。数値は当時の条件で得た結果であり、移行後の再ビルドに対する検証結果とは区別します。

履歴・測定記録・画像にあるLTC Playerや旧実行ファイル名は、名称変更前のものです。LTCueへの名称変更後に再検証したことを示しません。

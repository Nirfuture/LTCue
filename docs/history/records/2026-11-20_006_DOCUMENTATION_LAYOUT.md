# 資料の用途別フォルダー整理

2026-11-20。前回の履歴採番整理に続き、資料の探しやすさを改善した。

## 配置

- docs直下はREADMEのみ。現行資料19件をguides、development、reference、distribution、metadataに分類。
- history直下はREADMEのみ。既存本文58件をrecordsへ移動し、日付・番号・ファイル名を維持。
- imagesと版別のreleasesは元の配置を維持。

## 現行資料の移動対応

| 旧パス | 現在の資料 |
|---|---|
| `docs/QUICKSTART.md` | [docs/guides/QUICKSTART.md](../../guides/QUICKSTART.md) |
| `docs/USER_GUIDE.md` | [docs/guides/USER_GUIDE.md](../../guides/USER_GUIDE.md) |
| `docs/COMPATIBILITY.md` | [docs/guides/COMPATIBILITY.md](../../guides/COMPATIBILITY.md) |
| `docs/TROUBLESHOOTING.md` | [docs/guides/TROUBLESHOOTING.md](../../guides/TROUBLESHOOTING.md) |
| `docs/FINAL_SPEC.md` | [docs/development/FINAL_SPEC.md](../../development/FINAL_SPEC.md) |
| `docs/BUILD.md` | [docs/development/BUILD.md](../../development/BUILD.md) |
| `docs/VERIFICATION.md` | [docs/development/VERIFICATION.md](../../development/VERIFICATION.md) |
| `docs/HAP_PREPARATION.md` | [docs/reference/HAP_PREPARATION.md](../../development/implementation/HAP_PREPARATION.md) |
| `docs/LTC_SETTINGS.md` | [docs/reference/LTC_SETTINGS.md](../../development/implementation/LTC_SETTINGS.md) |
| `docs/OPERATION_ACCESS.md` | [docs/reference/OPERATION_ACCESS.md](../../development/implementation/OPERATION_ACCESS.md) |
| `docs/PLAYBACK_ERRORS.md` | [docs/reference/PLAYBACK_ERRORS.md](../../development/implementation/PLAYBACK_ERRORS.md) |
| `docs/SYNC_RECOVERY.md` | [docs/reference/SYNC_RECOVERY.md](../../development/implementation/SYNC_RECOVERY.md) |
| `docs/THEMING.md` | [docs/reference/THEMING.md](../../maintenance/THEMING.md) |
| `docs/DISTRIBUTION.md` | [docs/distribution/DISTRIBUTION.md](../../distribution/DISTRIBUTION.md) |
| `docs/RELEASING.md` | [docs/distribution/RELEASING.md](../../distribution/RELEASING.md) |
| `docs/DISTRIBUTION_README.md` | [docs/distribution/DISTRIBUTION_README.md](../../distribution/DISTRIBUTION_README.md) |
| `docs/asset-licenses.json` | [docs/metadata/asset-licenses.json](../../metadata/asset-licenses.json) |
| `docs/package-licenses.json` | [docs/metadata/package-licenses.json](../../metadata/package-licenses.json) |
| `docs/dependencies.json` | [docs/metadata/dependencies.json](../../metadata/dependencies.json) |

履歴本文は一律に `docs/history/<ファイル名>` → `docs/history/records/<同じファイル名>`。前回の履歴採番整理の対応表は[履歴索引](../README.md)を参照。

## 参照と配布

README、NOTICE、リリースノート、現行資料、履歴本文の参照先・画像の相対パス・mainを指すURLを更新。固定タグURL、当時のソースZIP内参照、実行ログや証跡のパスは歴史的記録として維持。配布用READMEはアプリ直下を基準とし、配布スクリプトの資料リストとライセンス確認パスを新配置に合わせた。

## 確認

77件の移動先、READMEのみの直下構成、ローカルMarkdownリンク・画像、JSONの内容維持を確認。配布対象15件（固定14件＋当該版の確認記録）を仮配置し、アプリ直下のREADME・NOTICE・リリースノートからの導線も点検。PowerShellの構文解析とgit diff --checkは通過。アプリの動作変更・ビルド・配布物再作成・実機試験・commit・pushは行っていない。

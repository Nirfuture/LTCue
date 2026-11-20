# 作業記録・旧仕様・計画の履歴

現在の基準は[現行仕様](../development/FINAL_SPEC.md)、[ビルド](../development/BUILD.md)、[検証手順](../development/VERIFICATION.md)、[版別確認記録](../releases)を参照。過去の合格は現在のHEADの合格を示さない。

本文は `records/` に集約し、このフォルダー直下には索引だけを置く。

履歴の旧コミットIDは移行前の開発履歴を識別します。旧製品名・実行ファイル名・測定環境・未確認事項は当時の記録として保存しています。

## 命名規則

`records/YYYY-MM-DD_NNN_内容.md`（JSONは拡張子を維持）。日付は主な作業・策定日、連番は日付ごとの資料の整理順で、作成時刻や厳密な実施順を保証しない。本文の依存関係を優先し、新規記録はその日の末尾へ追加する。通常の追加・追記で既存番号を振り直さない。複数日の追記は本文の日付を維持する。

## 日付順の記録一覧

| 日付 | 番号 | タイトル | 位置付け |
|---|---|---|---|
| 2026-11-09 | 001 | [LTC同期メディアプレイヤー 要件定義書](records/2026-11-09_001_LTC_Player_Requirements_v0.4.md) | 旧仕様・要件 |
| 2026-11-09 | 002 | [LTC同期メディアプレイヤー 開発指示書](records/2026-11-09_002_LTC_Player_Development_Instructions.md) | 計画・実装指示 |
| 2026-11-09 | 003 | [LTC Player 操作UI変更仕様 v0.1](records/2026-11-09_003_LTC_Player_UI_Change_Spec_v0.1.md) | 旧仕様・要件 |
| 2026-11-09 | 004 | [LTC Player 操作UI変更 実装手順書 v0.1](records/2026-11-09_004_LTC_Player_UI_Change_Implementation_v0.1.md) | 計画・実装指示 |
| 2026-11-10 | 001 | [配布前レビュー 0.1.0-preview.1](records/2026-11-10_001_PRE_RELEASE_REVIEW-preview.1.md) | 実装・調査・検証記録 |
| 2026-11-10 | 002 | [最終実装仕様 0.1.0-preview.1](records/2026-11-10_002_FINAL_SPEC-before-preview.10.md) | 旧仕様・要件 |
| 2026-11-10 | 003 | [LTC Player 配信操作仕様・検証計画 v0.2](records/2026-11-10_003_LTC_Player_Delivery_Operations_v0.2.md) | 実装・調査・検証記録 |
| 2026-11-10 | 004 | [起動時テスト4本登録仕様 v0.1](records/2026-11-10_004_LTC_Player_Default_Test_Show_v0.1.md) | 実装・調査・検証記録 |
| 2026-11-10 | 005 | [LTC Player 要件定義書 v0.5（再設計案）](records/2026-11-10_005_LTC_Player_Requirements_v0.5.md) | 旧仕様・要件 |
| 2026-11-10 | 006 | [LTC Player 再設計・実装計画 v0.5（提出案）](records/2026-11-10_006_LTC_Player_Implementation_Plan_v0.5.md) | 計画・実装指示 |
| 2026-11-10 | 007 | [再設計の進捗と検証記録](records/2026-11-10_007_REDESIGN_PROGRESS.md) | 実装・調査・検証記録 |
| 2026-11-10 | 008 | [共通キャンバス接続・出力分離の実装記録](records/2026-11-10_008_REDESIGN_CONNECTED_PLAYBACK.md) | 実装・調査・検証記録 |
| 2026-11-10 | 009 | [LTC Player 音声出力要件 v0.6](records/2026-11-10_009_LTC_Player_Audio_Requirements_v0.6.md) | 旧仕様・要件 |
| 2026-11-10 | 010 | [LTC Player 音声出力の実装計画 v0.6](records/2026-11-10_010_LTC_Player_Audio_Plan_v0.6.md) | 計画・実装指示 |
| 2026-11-10 | 011 | [音声出力v0.6：基盤実装・段階Aの検証記録](records/2026-11-10_011_AUDIO_FOUNDATION_PROGRESS.md) | 実装・調査・検証記録 |
| 2026-11-10 | 012 | [LTC Player 音声再生接続の実装・検証記録](records/2026-11-10_012_AUDIO_PLAYBACK_PROGRESS.md) | 実装・調査・検証記録 |
| 2026-11-10 | 013 | [LTC Player 音声出力UI・通常経路切替](records/2026-11-10_013_AUDIO_UI_PROGRESS.md) | 実装・調査・検証記録 |
| 2026-11-11 | 001 | [LTC Player 合意モックのUI実装](records/2026-11-11_001_APPROVED_UI_PROGRESS.md) | 実装・調査・検証記録 |
| 2026-11-11 | 002 | [キューリストの配置・操作修正](records/2026-11-11_002_QUEUE_LAYOUT_PROGRESS.md) | 実装・調査・検証記録 |
| 2026-11-11 | 003 | [LTC Player UI修正・プレビューFPS改善](records/2026-11-11_003_UI_POLISH_PROGRESS.md) | 実装・調査・検証記録 |
| 2026-11-11 | 004 | [キューファイルの場所を開く](records/2026-11-11_004_FILE_LOCATION_PROGRESS.md) | 実装・調査・検証記録 |
| 2026-11-11 | 005 | [LTC入力レベル表示](records/2026-11-11_005_INPUT_LEVEL_PROGRESS.md) | 実装・調査・検証記録 |
| 2026-11-11 | 006 | [入力レベルを現行の設定タブへ移動](records/2026-11-11_006_INPUT_LEVEL_SETTINGS_PROGRESS.md) | 実装・調査・検証記録 |
| 2026-11-11 | 007 | [音声ポン出しの開始遅延改善](records/2026-11-11_007_AUDIO_START_PROGRESS.md) | 実装・調査・検証記録 |
| 2026-11-11 | 008 | [LTC Player プレビュー・シーク・蓋画UIの整理](records/2026-11-11_008_PREVIEW_UI_PROGRESS.md) | 実装・調査・検証記録 |
| 2026-11-11 | 009 | [LTC Player PlaybackCoordinatorへの集約](records/2026-11-11_009_COORDINATOR_PROGRESS.md) | 実装・調査・検証記録 |
| 2026-11-11 | 010 | [設定の継続適用・未適用表示](records/2026-11-11_010_SETTINGS_APPLY_PROGRESS.md) | 実装・調査・検証記録 |
| 2026-11-11 | 011 | [最終工程前・第1段階：設定適用と未接続出力の修正](records/2026-11-11_011_SETTINGS_TRANSACTION_PROGRESS.md) | 実装・調査・検証記録 |
| 2026-11-11 | 012 | [設定整理・プレビュー下端の修正](records/2026-11-11_012_SETTINGS_EDGES_PROGRESS.md) | 実装・調査・検証記録 |
| 2026-11-11 | 013 | [最終工程前・第2段階：並べ替えと描画障害の扱い](records/2026-11-11_013_PLAYBACK_HEALTH_PROGRESS.md) | 実装・調査・検証記録 |
| 2026-11-11 | 014 | [最終工程前・第3段階：総合試験と一括検証](records/2026-11-11_014_UNIFIED_VERIFICATION_PROGRESS.md) | 実装・調査・検証記録 |
| 2026-11-11 | 015 | [最終工程前・第4段階：仕様と配布候補](records/2026-11-11_015_DISTRIBUTION_PREPARATION_PROGRESS.md) | 実装・調査・検証記録 |
| 2026-11-11 | 016 | [TEST_SUMMARY](records/2026-11-11_016_TEST_SUMMARY-preview.10.json) | 0.1.0-preview.10／記録日2026-11-11 |
| 2026-11-13 | 001 | [BUG-001: external display disconnect](records/2026-11-13_001_DEVICE_TEST_BUG001.md) | 実装・調査・検証記録 |
| 2026-11-13 | 002 | [検証環境B向け引き継ぎ（2026-11-13）](records/2026-11-13_002_HANDOFF.md) | 引き継ぎ |
| 2026-11-13 | 003 | [検証環境Aの再生開始遅延：調査・計測（2026-11-13）](records/2026-11-13_003_STARTUP_CHECK_ENV_A.md) | 実装・調査・検証記録 |
| 2026-11-13 | 004 | [Environment B startup comparison (2026-11-13)](records/2026-11-13_004_STARTUP_CHECK_ENV_B.md) | 実装・調査・検証記録 |
| 2026-11-13 | 005 | [検証環境A：FFmpegプロセス起動だけの小規模診断](records/2026-11-13_005_STARTUP_PROCESS_LAUNCH_ENV_A.md) | 実装・調査・検証記録 |
| 2026-11-15 | 001 | [実LTC入力・外部出力の確認記録](records/2026-11-15_001_PHYSICAL_LTC_TEST.md) | 実装・調査・検証記録 |
| 2026-11-16 | 001 | [外部出力の提示制御・表示改善の検証記録](records/2026-11-16_001_OUTPUT_PRESENTATION_CHECK.md) | 実装・調査・検証記録 |
| 2026-11-19 | 001 | [キューリストの解像度表示 実装指示書](records/2026-11-19_001_CUE_FORMAT_DISPLAY_SPEC.md) | 旧仕様・要件 |
| 2026-11-19 | 002 | [蓋絵連続フェードの性能低下：分析と対処方針](records/2026-11-19_002_COVER_FADE_ANALYSIS.md) | 実装・調査・検証記録 |
| 2026-11-19 | 003 | [蓋絵連続フェード：修正と検証](records/2026-11-19_003_COVER_FADE_VERIFICATION.md) | 実装・調査・検証記録 |
| 2026-11-19 | 004 | [環境A：出力表示ブランチの検証（2026-11-19）](records/2026-11-19_004_ENV_A_VERIFICATION.md) | 実装・調査・検証記録 |
| 2026-11-19 | 005 | [環境A：検証完了と再生開始遅延調査の引き継ぎ](records/2026-11-19_005_HANDOFF_ENV_A.md) | 引き継ぎ |
| 2026-11-19 | 006 | [環境A：HAP再生開始の予備計測（2026-11-19）](records/2026-11-19_006_STARTUP_HAP_ENV_A.md) | 実装・調査・検証記録 |
| 2026-11-19 | 007 | [再生開始遅延の改善方針（環境A、2026-11-19）](records/2026-11-19_007_STARTUP_IMPROVEMENT_PLAN.md) | 実装・調査・検証記録 |
| 2026-11-19 | 008 | [素材情報取得の共通化：実装・比較・検証（2026-11-19）](records/2026-11-19_008_SHARED_MEDIA_ANALYSIS.md) | 実装・調査・検証記録 |
| 2026-11-19 | 009 | [次セクションへの実装指示：素材情報取得の共通化のみ](records/2026-11-19_009_IMPLEMENT_MEDIA_INFO_SHARING.md) | 計画・実装指示 |
| 2026-11-19 | 010 | [音声起動待ちの調査と採用判断（2026-11-19）](records/2026-11-19_010_AUDIO_LAUNCH_INVESTIGATION.md) | 実装・調査・検証記録 |
| 2026-11-19 | 011 | [次セッションへの実装指示：48kHz MOV/PCM形式拡張と低水準解析の共通化](records/2026-11-19_011_IMPLEMENT_MOV_PCM_EXTENSION.md) | 計画・実装指示 |
| 2026-11-19 | 012 | [48kHz MOV/PCM形式拡張とMOV低水準共通化（2026-11-19）](records/2026-11-19_012_MOV_PCM_FORMATS.md) | 実装・調査・検証記録 |
| 2026-11-19 | 013 | [次セッションへの実装指示：MP4初回手動再生のFFmpeg二重起動の解消](records/2026-11-19_013_IMPLEMENT_MP4_SINGLE_DECODE_START.md) | 計画・実装指示 |
| 2026-11-20 | 001 | [MP4初回手動再生の単一復号（2026-11-20）](records/2026-11-20_001_MP4_SINGLE_DECODE_START.md) | 実装・調査・検証記録 |
| 2026-11-20 | 002 | [次セッションへの指示：キュー選択で上部操作ボタンが隠れる問題の修正](records/2026-11-20_002_IMPLEMENT_CUE_SELECTION_SCROLL_FIX.md) | 計画・実装指示 |
| 2026-11-20 | 003 | [キュー選択時の外側スクロール修正（2026-11-20）](records/2026-11-20_003_CUE_SELECTION_SCROLL_FIX.md) | 実装・調査・検証記録 |
| 2026-11-20 | 004 | [作業指示書：作業記録と検証資料の整理](records/2026-11-20_004_IMPLEMENT_DOCUMENTATION_CLEANUP.md) | 計画・実装指示 |
| 2026-11-20 | 005 | [検証手順から分離した過去記録](records/2026-11-20_005_VERIFICATION_PAST_RECORDS.md) | 整理日は11月20日、実施日は本文参照 |
| 2026-11-20 | 006 | [資料の用途別フォルダー整理](records/2026-11-20_006_DOCUMENTATION_LAYOUT.md) | 現行資料の分類とhistory/recordsへの集約 |
| 2026-11-20 | 007 | [技術解説の現行説明への整理](records/2026-11-20_007_TECHNICAL_REFERENCE_CLEANUP.md) | 変更経緯の分離と現行コードとの照合 |
| 2026-11-20 | 008 | [現行仕様と技術解説6件の照合](records/2026-11-20_008_REFERENCE_SPEC_COMPARISON.md) | 重複・固有情報・要修正事項と統合案のレビュー |
| 2026-11-20 | 009 | [現行仕様・実装補足・保守手順の整理](records/2026-11-20_009_SPEC_IMPLEMENTATION_REORGANIZATION.md) | 重複の集約、記述訂正と配置変更 |

## 移動対応表

以下の旧パスは整理前の所在を示す履歴であり、現在開くためのパスではない。MP4実装指示は11月19日深夜の素材作成後、翌日の実装前に位置付けた。OUTPUT_PRESENTATION_CHECKは11月15〜16日の確認、11月19日の記録整理を本文に維持する。

| 旧パス | 現在の資料 |
|---|---|
| `docs/history/LTC_Player_Requirements_v0.4.md` | [2026-11-09_001_LTC_Player_Requirements_v0.4.md](records/2026-11-09_001_LTC_Player_Requirements_v0.4.md) |
| `docs/history/LTC_Player_Development_Instructions.md` | [2026-11-09_002_LTC_Player_Development_Instructions.md](records/2026-11-09_002_LTC_Player_Development_Instructions.md) |
| `docs/history/LTC_Player_UI_Change_Spec_v0.1.md` | [2026-11-09_003_LTC_Player_UI_Change_Spec_v0.1.md](records/2026-11-09_003_LTC_Player_UI_Change_Spec_v0.1.md) |
| `docs/history/LTC_Player_UI_Change_Implementation_v0.1.md` | [2026-11-09_004_LTC_Player_UI_Change_Implementation_v0.1.md](records/2026-11-09_004_LTC_Player_UI_Change_Implementation_v0.1.md) |
| `docs/history/PRE_RELEASE_REVIEW-preview.1.md` | [2026-11-10_001_PRE_RELEASE_REVIEW-preview.1.md](records/2026-11-10_001_PRE_RELEASE_REVIEW-preview.1.md) |
| `docs/history/FINAL_SPEC-before-preview.10.md` | [2026-11-10_002_FINAL_SPEC-before-preview.10.md](records/2026-11-10_002_FINAL_SPEC-before-preview.10.md) |
| `docs/history/LTC_Player_Delivery_Operations_v0.2.md` | [2026-11-10_003_LTC_Player_Delivery_Operations_v0.2.md](records/2026-11-10_003_LTC_Player_Delivery_Operations_v0.2.md) |
| `docs/history/LTC_Player_Default_Test_Show_v0.1.md` | [2026-11-10_004_LTC_Player_Default_Test_Show_v0.1.md](records/2026-11-10_004_LTC_Player_Default_Test_Show_v0.1.md) |
| `docs/history/LTC_Player_Requirements_v0.5.md` | [2026-11-10_005_LTC_Player_Requirements_v0.5.md](records/2026-11-10_005_LTC_Player_Requirements_v0.5.md) |
| `docs/history/LTC_Player_Implementation_Plan_v0.5.md` | [2026-11-10_006_LTC_Player_Implementation_Plan_v0.5.md](records/2026-11-10_006_LTC_Player_Implementation_Plan_v0.5.md) |
| `docs/history/REDESIGN_PROGRESS.md` | [2026-11-10_007_REDESIGN_PROGRESS.md](records/2026-11-10_007_REDESIGN_PROGRESS.md) |
| `docs/history/REDESIGN_CONNECTED_PLAYBACK.md` | [2026-11-10_008_REDESIGN_CONNECTED_PLAYBACK.md](records/2026-11-10_008_REDESIGN_CONNECTED_PLAYBACK.md) |
| `docs/history/LTC_Player_Audio_Requirements_v0.6.md` | [2026-11-10_009_LTC_Player_Audio_Requirements_v0.6.md](records/2026-11-10_009_LTC_Player_Audio_Requirements_v0.6.md) |
| `docs/history/LTC_Player_Audio_Plan_v0.6.md` | [2026-11-10_010_LTC_Player_Audio_Plan_v0.6.md](records/2026-11-10_010_LTC_Player_Audio_Plan_v0.6.md) |
| `docs/history/AUDIO_FOUNDATION_PROGRESS.md` | [2026-11-10_011_AUDIO_FOUNDATION_PROGRESS.md](records/2026-11-10_011_AUDIO_FOUNDATION_PROGRESS.md) |
| `docs/history/AUDIO_PLAYBACK_PROGRESS.md` | [2026-11-10_012_AUDIO_PLAYBACK_PROGRESS.md](records/2026-11-10_012_AUDIO_PLAYBACK_PROGRESS.md) |
| `docs/history/AUDIO_UI_PROGRESS.md` | [2026-11-10_013_AUDIO_UI_PROGRESS.md](records/2026-11-10_013_AUDIO_UI_PROGRESS.md) |
| `docs/history/APPROVED_UI_PROGRESS.md` | [2026-11-11_001_APPROVED_UI_PROGRESS.md](records/2026-11-11_001_APPROVED_UI_PROGRESS.md) |
| `docs/history/QUEUE_LAYOUT_PROGRESS.md` | [2026-11-11_002_QUEUE_LAYOUT_PROGRESS.md](records/2026-11-11_002_QUEUE_LAYOUT_PROGRESS.md) |
| `docs/history/UI_POLISH_PROGRESS.md` | [2026-11-11_003_UI_POLISH_PROGRESS.md](records/2026-11-11_003_UI_POLISH_PROGRESS.md) |
| `docs/history/FILE_LOCATION_PROGRESS.md` | [2026-11-11_004_FILE_LOCATION_PROGRESS.md](records/2026-11-11_004_FILE_LOCATION_PROGRESS.md) |
| `docs/history/INPUT_LEVEL_PROGRESS.md` | [2026-11-11_005_INPUT_LEVEL_PROGRESS.md](records/2026-11-11_005_INPUT_LEVEL_PROGRESS.md) |
| `docs/history/INPUT_LEVEL_SETTINGS_PROGRESS.md` | [2026-11-11_006_INPUT_LEVEL_SETTINGS_PROGRESS.md](records/2026-11-11_006_INPUT_LEVEL_SETTINGS_PROGRESS.md) |
| `docs/history/AUDIO_START_PROGRESS.md` | [2026-11-11_007_AUDIO_START_PROGRESS.md](records/2026-11-11_007_AUDIO_START_PROGRESS.md) |
| `docs/history/PREVIEW_UI_PROGRESS.md` | [2026-11-11_008_PREVIEW_UI_PROGRESS.md](records/2026-11-11_008_PREVIEW_UI_PROGRESS.md) |
| `docs/history/COORDINATOR_PROGRESS.md` | [2026-11-11_009_COORDINATOR_PROGRESS.md](records/2026-11-11_009_COORDINATOR_PROGRESS.md) |
| `docs/history/SETTINGS_APPLY_PROGRESS.md` | [2026-11-11_010_SETTINGS_APPLY_PROGRESS.md](records/2026-11-11_010_SETTINGS_APPLY_PROGRESS.md) |
| `docs/history/SETTINGS_TRANSACTION_PROGRESS.md` | [2026-11-11_011_SETTINGS_TRANSACTION_PROGRESS.md](records/2026-11-11_011_SETTINGS_TRANSACTION_PROGRESS.md) |
| `docs/history/SETTINGS_EDGES_PROGRESS.md` | [2026-11-11_012_SETTINGS_EDGES_PROGRESS.md](records/2026-11-11_012_SETTINGS_EDGES_PROGRESS.md) |
| `docs/history/PLAYBACK_HEALTH_PROGRESS.md` | [2026-11-11_013_PLAYBACK_HEALTH_PROGRESS.md](records/2026-11-11_013_PLAYBACK_HEALTH_PROGRESS.md) |
| `docs/history/UNIFIED_VERIFICATION_PROGRESS.md` | [2026-11-11_014_UNIFIED_VERIFICATION_PROGRESS.md](records/2026-11-11_014_UNIFIED_VERIFICATION_PROGRESS.md) |
| `docs/history/DISTRIBUTION_PREPARATION_PROGRESS.md` | [2026-11-11_015_DISTRIBUTION_PREPARATION_PROGRESS.md](records/2026-11-11_015_DISTRIBUTION_PREPARATION_PROGRESS.md) |
| `docs/TEST_SUMMARY.json` | [2026-11-11_016_TEST_SUMMARY-preview.10.json](records/2026-11-11_016_TEST_SUMMARY-preview.10.json) |
| `docs/DEVICE_TEST_BUG001.md` | [2026-11-13_001_DEVICE_TEST_BUG001.md](records/2026-11-13_001_DEVICE_TEST_BUG001.md) |
| `docs/HANDOFF_2026-11-13.md` | [2026-11-13_002_HANDOFF.md](records/2026-11-13_002_HANDOFF.md) |
| `docs/STARTUP_CHECK_ENV_A.md` | [2026-11-13_003_STARTUP_CHECK_ENV_A.md](records/2026-11-13_003_STARTUP_CHECK_ENV_A.md) |
| `docs/STARTUP_CHECK_ENV_B.md` | [2026-11-13_004_STARTUP_CHECK_ENV_B.md](records/2026-11-13_004_STARTUP_CHECK_ENV_B.md) |
| `docs/STARTUP_PROCESS_LAUNCH_ENV_A.md` | [2026-11-13_005_STARTUP_PROCESS_LAUNCH_ENV_A.md](records/2026-11-13_005_STARTUP_PROCESS_LAUNCH_ENV_A.md) |
| `docs/PHYSICAL_LTC_TEST_2026-11-15.md` | [2026-11-15_001_PHYSICAL_LTC_TEST.md](records/2026-11-15_001_PHYSICAL_LTC_TEST.md) |
| `docs/OUTPUT_PRESENTATION_CHECK_2026-11-16.md` | [2026-11-16_001_OUTPUT_PRESENTATION_CHECK.md](records/2026-11-16_001_OUTPUT_PRESENTATION_CHECK.md) |
| `docs/CUE_FORMAT_DISPLAY_SPEC.md` | [2026-11-19_001_CUE_FORMAT_DISPLAY_SPEC.md](records/2026-11-19_001_CUE_FORMAT_DISPLAY_SPEC.md) |
| `docs/COVER_FADE_ANALYSIS_2026-11-19.md` | [2026-11-19_002_COVER_FADE_ANALYSIS.md](records/2026-11-19_002_COVER_FADE_ANALYSIS.md) |
| `docs/COVER_FADE_VERIFICATION_2026-11-19.md` | [2026-11-19_003_COVER_FADE_VERIFICATION.md](records/2026-11-19_003_COVER_FADE_VERIFICATION.md) |
| `docs/ENV_A_VERIFICATION_2026-11-19.md` | [2026-11-19_004_ENV_A_VERIFICATION.md](records/2026-11-19_004_ENV_A_VERIFICATION.md) |
| `docs/HANDOFF_ENV_A_2026-11-19.md` | [2026-11-19_005_HANDOFF_ENV_A.md](records/2026-11-19_005_HANDOFF_ENV_A.md) |
| `docs/STARTUP_HAP_ENV_A_2026-11-19.md` | [2026-11-19_006_STARTUP_HAP_ENV_A.md](records/2026-11-19_006_STARTUP_HAP_ENV_A.md) |
| `docs/STARTUP_IMPROVEMENT_PLAN_2026-11-19.md` | [2026-11-19_007_STARTUP_IMPROVEMENT_PLAN.md](records/2026-11-19_007_STARTUP_IMPROVEMENT_PLAN.md) |
| `docs/SHARED_MEDIA_ANALYSIS_2026-11-19.md` | [2026-11-19_008_SHARED_MEDIA_ANALYSIS.md](records/2026-11-19_008_SHARED_MEDIA_ANALYSIS.md) |
| `docs/IMPLEMENT_MEDIA_INFO_SHARING_2026-11-19.md` | [2026-11-19_009_IMPLEMENT_MEDIA_INFO_SHARING.md](records/2026-11-19_009_IMPLEMENT_MEDIA_INFO_SHARING.md) |
| `docs/AUDIO_LAUNCH_INVESTIGATION_2026-11-19.md` | [2026-11-19_010_AUDIO_LAUNCH_INVESTIGATION.md](records/2026-11-19_010_AUDIO_LAUNCH_INVESTIGATION.md) |
| `docs/IMPLEMENT_MOV_PCM_EXTENSION_2026-11-19.md` | [2026-11-19_011_IMPLEMENT_MOV_PCM_EXTENSION.md](records/2026-11-19_011_IMPLEMENT_MOV_PCM_EXTENSION.md) |
| `docs/MOV_PCM_FORMATS_2026-11-19.md` | [2026-11-19_012_MOV_PCM_FORMATS.md](records/2026-11-19_012_MOV_PCM_FORMATS.md) |
| `docs/IMPLEMENT_MP4_SINGLE_DECODE_START.md` | [2026-11-19_013_IMPLEMENT_MP4_SINGLE_DECODE_START.md](records/2026-11-19_013_IMPLEMENT_MP4_SINGLE_DECODE_START.md) |
| `docs/MP4_SINGLE_DECODE_START_2026-11-20.md` | [2026-11-20_001_MP4_SINGLE_DECODE_START.md](records/2026-11-20_001_MP4_SINGLE_DECODE_START.md) |
| `docs/IMPLEMENT_CUE_SELECTION_SCROLL_FIX_2026-11-20.md` | [2026-11-20_002_IMPLEMENT_CUE_SELECTION_SCROLL_FIX.md](records/2026-11-20_002_IMPLEMENT_CUE_SELECTION_SCROLL_FIX.md) |
| `docs/CUE_SELECTION_SCROLL_FIX_2026-11-20.md` | [2026-11-20_003_CUE_SELECTION_SCROLL_FIX.md](records/2026-11-20_003_CUE_SELECTION_SCROLL_FIX.md) |
| `docs/IMPLEMENT_DOCUMENTATION_CLEANUP.md` | [2026-11-20_004_IMPLEMENT_DOCUMENTATION_CLEANUP.md](records/2026-11-20_004_IMPLEMENT_DOCUMENTATION_CLEANUP.md) |

## 整理完了時の確認（2026-11-20）

57件を移動・改名（既存履歴32件、docs直下の作業記録23件、TEST_SUMMARY 1件、本整理指示書1件）。検証手順から分離した過去記録を1件追加した。全移動先の存在、日付ごとの連番、ローカルMarkdown・画像リンク、JSON読込と元データ一致、配布資料14件と版別記録の存在を確認。アプリ・ランナー・カタログ・試験コードは変更せず、実行試験・配布物作成・commit・pushは行っていない。

旧パスが残る箇所は、この対応表、再設計進捗の当時の配置記録、preview.10記録の当時のソースZIP内参照。固定タグURL・実行ログ・証跡パスも当時の記録として維持する。mainを指す診断資料のURLは移動先へ更新した。ローカルリンクの既存不備・新規不備はいずれも検出されなかった。

## 用途別フォルダーへの整理

現行資料の配置は[資料の案内](../README.md)を参照。履歴本文58件はファイル名を維持して `history/records/` へ移動した。上記の旧パスは前回整理前の所在を示し、「現在の資料」のリンクは今回の配置へ更新している。

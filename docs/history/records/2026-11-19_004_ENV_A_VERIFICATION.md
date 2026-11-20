# 環境A：出力表示ブランチの検証（2026-11-19）

## 最終結果

ユーザー承認後の修正を含む最終検証版で、正式な`scripts/test.ps1`が終了コード0。計算系4＋Auto 51の**55スイートすべてPASS**、ランナー集計550項目。性能サンプル等も含むため、550個の独立した単体テストを意味しない。通常版・検証版のビルド、ランナー保守9項目、`git diff --check`も成功。

最終一括証跡：`artifacts/tests-env-a-final-v2-20261119/summary.json`と`manifest.json`。検証版：`artifacts/env-a-final-v2/app/`。通常版：`artifacts/env-a-final-v2/product/`。ソース・検証バイナリのSHA-256：`artifacts/env-a-final-v2/source-manifest.json`。ランナー保守：`artifacts/env-a-final-v2/runner-checks/runner-checks.json`。

この最終結果は1回の正式な一括実行で得たものであり、途中の失敗結果を合格へ書き換えたり、別々の試行を合算したりしていない。最終一括実行は、専用ビルドと生成済み9素材を`-SkipBuild`で指定し、カタログ一致・ランタイム同梱・全素材のハッシュを検査してから、新規の結果フォルダーへ実行した。

変更は検証コード、テスト素材生成、ランナー、資料に限定。製品の再生・同期・設定形式は変更していない。成果は検証用ブランチで管理し、開始遅延調査の現在地は[引き継ぎ資料](2026-11-19_005_HANDOFF_ENV_A.md)へ記録した。

## 対象と環境

- 基点：`fix/output-presentation`、`7cc237245eb2f056b3077bc1ec7fbbe1a09db5fd`。
- 作業ブランチ：`test/env-a-output-presentation-20261119`。開始時点で基点から作成済み、作業ツリーはクリーン。
- CPU：Intel Core i9-14900KF、32論理CPU。Turbo Boost無効はユーザー申告。BIOS設定は変更していない。
- GPU：NVIDIA GeForce RTX 4090、ドライバー31.0.15.3667。電源プラン：高パフォーマンス。
- 縦置き1080×1920（DISPLAY1）と横置き3840×2160（DISPLAY2）の2ディスプレイ。最終の画面別検証は両ディスプレイでPASS。
- PowerShell 7.6.9。.NET SDK 10.0.203をMicrosoft公式インストーラーで通常導入（終了コード0）。既存SDKは削除していない。
- FFmpeg / ffprobe 8.1.2 full build（gyan.dev）。ユーザーの`%LOCALAPPDATA%/Programs/FFmpeg/8.1.2-full/`へ配置し、ユーザーPATHへ追加。配布元SHA-256を照合済み。libx264、AAC、HAP、PCM、libmp3lameを確認。
- SDKインストーラーはMicrosoft公式メタデータのSHA-512を照合済み。
- 当初のリポジトリ専用SDK・FFmpeg・ダウンロードZIPは、通常導入へ切り替える指示に従い削除。開始前からあった`artifacts/tools/dotnet-install.ps1`は保持。

## 実行

`docs/development/VERIFICATION.md`に従い、`scripts/test.ps1`で専用self-containedビルド、素材生成、計算系4スイートとAuto 51スイートを実行。結果フォルダーは新規作成、既存設定・証跡は再利用・上書きしていない。

通常版ビルドも成功。ランナー修正後の`scripts/test-verification-runner.ps1`は9項目PASS。`git diff --check`も成功。

## 初回結果

55スイートを完走し、49 PASS・6 FAIL。一括検証全体は不合格。途中の失敗後も後続を実行した。

| スイート | 失敗内容 |
|---|---|
| timecode-editing-calculation | 実テスト32項目すべてPASS、ランナーが31項目を期待していたためFAIL |
| timing-status | LTC復帰後の遅延表示待ちでタイムアウト。表示は映像・音声とも待機中 |
| settings-apply | 再適用後の設定全体の等値比較が不一致 |
| output-mode | 設定保存・読込の等値比較、および標準・互換の提示フレーム数条件が不成立 |
| portrait-monitor | 両ディスプレイで`stats wording`が不一致。試験はスキップ数ツールチップを要求しているが、現行仕様では撤去済み |
| phase2-adapter | 入力設定の保存・読込後の等値比較が不一致 |

縦置き試験は映像開始、比率、領域内配置、解像度ラベルの確認を通過してから文言照合で失敗。これをスイート全体の合格とは扱わない。

## 承認済み修正

`7fdce73`で重複警告テストが追加された際の期待件数更新漏れを確認。ユーザー承認に基づき`test.ps1`の期待件数を31から32へ変更し、`VERIFICATION.md`の現行対象説明も更新した。過去の31項目合格記録は書き換えていない。

計算テストを新しいフォルダーで再実行し、32/32 PASS・終了コード0を確認。ランナー保守9項目もPASS。初回の`summary.json`は修正せず保持した。件数以外の機能・試験修正は行っていない。

## 機能スイートの再実行

同じ検証バイナリ・同じ素材を使い、別の設定と結果フォルダーで5スイートを順次再実行。5スイートともFAILが再現した。一過性として合格扱いにはしていない。この時点では追加修正の判断をユーザーへ依頼した。

最初の個別再実行コマンドは引数連結の誤りで試験入口に入らず、120秒で専用プロセスのみ終了した。この試行は`recheck/`に残し、再現性確認には使用しない。引数を修正した`recheck-v2/`が有効な再実行証跡。通常の一括ランナーにはこの引数誤りはない。

## 追加承認後の修正

ユーザーの追加承認を受けて原因を調査。製品処理の変更は不要で、検証コード・素材・ランナーを修正した。

- 設定保存・読込の3スイート：`CueFormatPreferences.Presets`は配列であり、recordの等値比較は配列の参照を比較していた。検証専用ヘルパーで全設定のJSON値を比較する。異なる出力方式、プリセット順、プリセット名が不一致になることも検査する。製品の等値比較・設定形式は変更しない。
- 出力方式：固定TC `00:00:02;00`の繰返しでは同じ復号フレームの提示が省かれるため、複数提示を要求する試験と矛盾していた。進む合成LTCへ修正し、提示数条件、実際の出力方式、UI、保存値、出力中の変更拒否は維持した。
- 縦置き：累計スキップ数ツールチップが存在するという旧期待値を、現行仕様の撤去確認へ修正。映像開始、出力比率、収まり、解像度表示、出力ボタンは従来どおり検査する。
- LTC復帰：元の6秒素材では診断時のTCが約10.5秒に進み、キューが終わっていた。30秒の映像・音声付き素材を生成し、素材の存在・ハッシュを一括ランナーでも確認する。
- 30秒素材でも初回準備が6秒を超える場合があった。初回同期と喪失後の復帰だけ待ち上限を15秒に変更し、待機時間と状態を`waits.json`へ記録。単独確認では初回6.83秒、復帰2.67秒で遅延なしへ移行し、11項目PASS。製品の遅延判定しきい値・継続時間は変更していない。

最初の修正版一括検証は54 PASS・1 FAIL（初回同期の6秒上限）。その証跡は`artifacts/tests-env-a-fixed-20261119/`へ保持。待ち上限変更後の単独11項目PASSは`artifacts/env-a-timing-waits/checks/`。これらを最終一括結果と混ぜずに保存する。

この段階の通常版ビルドは`artifacts/env-a-fixed-product/`。ランナー保守用9項目は`artifacts/env-a-fixed-runner-checks/runner-checks.json`でPASS。検証専用ヘルパーは通常版ではコンパイル対象外。最終版の配置は冒頭を参照。

## 追加で見つかったLTC設定試験の競合

待ち上限変更後の一括検証`artifacts/tests-env-a-final-20261119/`では、元の5スイートはすべてPASSしたが、`ltc-settings`の補正値変更試験が1項目失敗した（全体54 PASS・1 FAIL）。

旧待機条件は、新しい同期セッションIDに切り替わったことと、画面側にキャッシュされた映像観測・音声Playingを確認していた。コーディネーターはセッションIDを公開してから、非同期に映像の所有権取得と実行Taskの登録を行う。この間、画面側には前セッションのフレームが残り、音声側にも前の状態が残り得るため、新セッションの準備を確認したことにならなかった。

検証側で現在のコーディネーター自身の観測、新セッションと一致する音声プレイヘッド、観測時刻以降の音声Playingを待つよう修正。補正位置の許容差0.15秒、時計・入力開始回数・音声出力セッション・キャンバス維持の条件は変更していない。比較に使用した個別値を`offset-transition.json`へ保存する。

タイミング表示試験も最初から30秒素材を登録する構成に整理し、途中で素材を差し替えることによる一覧の古い表示を避けた。製品コードの変更はない。

修正後、`ltc-settings`22項目と`timing-status`11項目を新規プロセス・新規設定で3回ずつ実行し、全回PASS。証跡は`artifacts/env-a-final-v2/recheck-1/`～`recheck-3/`に保持。検証版は`artifacts/env-a-final-v2/app/`、対象ソースとバイナリの記録は同フォルダーの親にある`source-manifest.json`。

最終一括実行でも`ltc-settings`22項目、`timing-status`11項目はPASS。タイミング表示の初回安定待ちは3.90秒、喪失後の復帰待ちは2.69秒（`timing-status/waits.json`）。手動再生・停止を含む同一試験内の値で、ディスク完全コールド開始や物理AV遅延の計測ではない。`ltc-settings/offset-transition.json`に新旧同期ID、入力開始回数、音声出力・キャンバスID、補正後の位置を保存。

`cover-height/initial.png`、`save-layout/default-layout.png`、`timing-status/timing-status-sync-fps.png`を目視確認。一覧と操作部の重なり・はみ出しは見られなかった。GPUプレビューの描画結果はRenderTargetBitmapによるUI画像だけで判定せず、描画系スイートの画素検証も併用した。

## 証跡

Git管理対象外の`artifacts/tests-env-a-20261119-174358/`に保存。

- `environment.json`：構成、ツール、ハッシュ。
- `console.log`、`run/manifest.json`、`run/summary.json`：初回一括検証。
- `run/<suite>/`：各試験のJSON、ログ、画像。
- `timecode-count-recheck/`：32項目の再実行。
- `runner-checks/runner-checks.json`：ランナー保守試験9項目。
- `product-build.log`：通常版ビルド。
- `recheck/`：個別再実行コマンドの誤起動。試験結果として無効。
- `recheck-v2/summary.json`および各スイート：失敗した機能5スイートの有効な再実行。

## 範囲外

実LTC入力、物理AV同期、ケーブルの抜き差し、実素材の全尺性能、2時間以上の耐久は未実施。合成入力と小さい出力ウィンドウによる自動試験を、それらの合格とは扱わない。

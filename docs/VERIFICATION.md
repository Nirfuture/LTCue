# 一括検証

2026-11-13。最終候補の自動検証には`./scripts/test.ps1`を使用する。古い個別スクリプトで一部を通した結果を、全体の合格とは扱わない。

## 実行条件

- Windows x64、D3D11対応GPU、接続済みディスプレイ、WASAPIの音声出力先。
- PowerShell 7.2以降。リポジトリのglobal.jsonに対応した.NET SDK、Visual StudioのMSVC x64とWindows SDK。
- FFmpeg/ffprobeをPATHへ配置。libx264、AAC、HAP、PCM、libmp3lameのエンコーダーが必要。
- 実行中の製品版を終了する必要はない。他の検証版が動いている場合は開始せず通知する。

通常のビルドと実行：

```powershell
./scripts/test.ps1
```

別のdotnetを使う場合：

```powershell
./scripts/test.ps1 -DotnetPath C:/path/to/dotnet.exe
```

検証版は新規の結果フォルダー内へランタイム同梱で発行する。通常の製品版・検証版の既存フォルダーを上書きしない。結果フォルダーが既にある場合も停止する。

既にビルドした候補を再検証する場合：

```powershell
./scripts/test.ps1 -SkipBuild `
  -DotnetPath C:/path/to/dotnet.exe `
  -ExecutablePath C:/path/to/verification/LTCue.exe `
  -SyncRecoveryAssemblyPath C:/path/to/SyncRecovery.dll `
  -ResultPath C:/path/to/new-results
```

`-SkipBuild`でも専用の検証版が必要。ランタイムを同梱しないビルドや通常版は、起動前の検査で拒否する。CPU同期試験のDLLも事前にビルドしておく。

素材は既定で新規生成する。再利用する場合は`-FixturesPath`と`-AudioFixturesPath`を両方指定する。必要な全ファイルを開始前に検査する。いずれの場合も使用した素材のハッシュを記録する。

各版で実際に実行した範囲・失敗・再試験は[確認記録](releases)へ記載する。以下の対象一覧は、最新ソースで全件合格したことを示すものではない。

## 合格の条件

試験一覧は`scripts/verification-suites.json`に一元化し、検証版にも埋め込む。実行ファイル内の一覧がソース側と異なる場合は、古いビルドとして開始前に拒否する。通常版の判定にはPEメタデータを読み、判定のためにアプリを起動しない。

各試験は別プロセス・別設定・別証跡フォルダーで順番に実行する。画面提示を確認するため、検証版のウィンドウは前面に表示する。合格には終了コード0だけでなく、証跡の存在、形式、全項目の結果、予定件数が必要。空の結果や一部だけの実行を合格にしない。ディスプレイ数で項目数が変わる試験は、非空の結果と全画面の合否を確認する。

1試験の既定上限は120秒。期限超過時は、その試験のために起動したプロセスツリーだけを終了する。製品版や他のアプリを終了しない。試験が失敗しても後続を実行し、全体として失敗終了する。

結果フォルダー：

```text
manifest.json       使用バイナリ・素材のSHA-256、実行対象外の理由
summary.json        合否・件数・終了コード・実行時間・失敗理由
sync-calculation/   CPUの同期計算7シナリオ
各試験名/           独立設定、JSON、画像、stdout.log、stderr.log
runner-failure.txt  全体が失敗した場合の記録
```

自動対象はアプリ36スイートとCPU同期計算1スイート。件数にはリサイズ40ケース、性能測定、音声開始の測定ケースも含まれるため、すべてを独立した単体テスト数と解釈しない。接続ディスプレイ数によって合計件数は変わる。

## 主な検証経路

- preview-recovery：製品と同じ共通GPUキャンバスを使い、色、保持、プレビュー非表示からの復帰、LTC喪失・再開、出力停止時のセッション維持を確認。
- default-show：4キューの動作に加え、黒区間で出力を再開しても時計が戻らず、その後のLTCで次のキューへ進むことを確認。合成DF信号も禁止ラベルを生成しない。
- manual-output：古い保持状態を拾わず、今回の再生開始→終端→保持の順に確認。出力オン・オフだけで同じ映像を保持することも画素比較する。
- 時刻差素材：早い音声はステレオ・映像PTS維持で生成。非ゼロ時刻のAACは高精度のコンテナ時刻を指定し、丸め誤差を試験素材へ混ぜない。

低レベルの旧レンダラー試験は部品の回帰として残す。製品経路そのものの検証は共通キャンバス・再生統合・復帰試験で行う。

## 開始時間の限定計測

専用検証版の`--verify-startup-measure`は、手動再生の初回と停止後2回を計測する。一括試験とは別の診断入口で、開始待ち時間と開始後の内部同期誤差を分けて扱う。提示API・WASAPI Startの時刻を物理的な表示・出音時刻とは扱わない。条件と結果は[検証環境Aの記録](STARTUP_CHECK_ENV_A.md)を参照。

## 対象外と保守用検証

`live-startup`は外部の実LTC源が必要なため手動試験。`redesign-baseline`は再設計前の履歴で、現行製品の受入試験に含めない。どちらも理由をmanifestへ記録し、自動合格数には含めない。

実際の機器切断、実LTC入力、他の環境、2時間以上の耐久、物理ディスプレイと出音の遅延は別工程。小さい出力ウィンドウや失敗注入による検証を、それらの実測の代用にしない。

ランナーを変更した場合は、保守用の誤判定防止試験も行う。

```powershell
./scripts/test-verification-runner.ps1 `
  -ProductAssemblyPath C:/path/to/product/LTCue.dll `
  -ResultPath C:/path/to/new-runner-checks
```

完全な証跡の受入、空・不足・失敗・欠落・fatal記録の拒否、通常版の起動前拒否、既存結果フォルダーの拒否、専用子プロセスのタイムアウトを確認する。

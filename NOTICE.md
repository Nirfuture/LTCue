# ライセンスと第三者コンポーネント

## LTCueのコード

LTCue独自のC#アプリケーションコードと`native/phase5`のコードは、MITライセンスで提供します。著作権表記とライセンス本文は[LICENSE](LICENSE)を参照してください。

以下のLGPL対象コードおよび第三者コンポーネント・素材には、それぞれのライセンスが適用されます。

## AudioNative.dllとlibltc

[`native/Audio.cpp`](https://github.com/Nirfuture/LTCue/blob/main/native/Audio.cpp)とlibltcは、GNU Lesser General Public Licenseのバージョン3、または利用者が選択するそれ以降のバージョン（`LGPL-3.0-or-later`）で提供します。

`AudioNative.dll`はlibltcを静的に組み込んでおり、`LGPL-3.0-or-later`で配布します。これらの部分には、アプリケーションのMITライセンスは適用されません。

ライセンス本文と著作権表示は、次を参照してください。

- [LICENSES/LGPL-3.0.txt](LICENSES/LGPL-3.0.txt)
- [LICENSES/GPL-3.0.txt](LICENSES/GPL-3.0.txt)
- libltcの[COPYING](https://github.com/Nirfuture/LTCue/blob/main/third_party/libltc/COPYING)および[各ソースファイル](https://github.com/Nirfuture/LTCue/tree/main/third_party/libltc/src)のヘッダー。実行用ZIPでは`CorrespondingSource/third_party/libltc/`以下に収録しています。

### libltcの出典

- 公式リポジトリ：[x42/libltc](https://github.com/x42/libltc)
- バージョン：1.3.2
- 上流コミット：[`bf84b01097a1789c0296cc5fcfc3bf4608407930`](https://github.com/x42/libltc/commit/bf84b01097a1789c0296cc5fcfc3bf4608407930)

収録した上流ファイルは未改変で、元の著作権・ライセンス表示を保持しています。上流リポジトリの配布補助ファイルなどは一部省略しています。

### 対応ソースとDLLの差し替え

ソースリポジトリでは、次のファイルから`AudioNative.dll`を再ビルド・再リンクできます。

- [native/Audio.cpp](https://github.com/Nirfuture/LTCue/blob/main/native/Audio.cpp)
- [third_party/libltc/](https://github.com/Nirfuture/LTCue/tree/main/third_party/libltc)
- [native/build-phase2.cmd](https://github.com/Nirfuture/LTCue/blob/main/native/build-phase2.cmd)

実行用ZIPには、これらを`CorrespondingSource/`以下に同梱しています。対応するソースの識別情報とビルド方法は、ZIP内の`CorrespondingSource/README.md`を参照してください。

利用者はLGPL対象部分を改変し、再ビルドした`AudioNative.dll`へ差し替えられます。既存のアプリで使用する場合は、アプリとのABI互換性を維持してください。本配布の条件は、これらの改変や、改変部分のデバッグを目的とするリバースエンジニアリングを制限しません。

改変物を再配布する場合は、適用されるライセンスに従い、必要な表示の保持や対応ソースの提供などを行ってください。

## NuGetパッケージと.NETランタイム

NuGetパッケージのライセンスと表示は、[LICENSES/NuGet/](LICENSES/NuGet/)および[docs/metadata/package-licenses.json](docs/metadata/package-licenses.json)に収録しています。

Skia、HarfBuzz、ANGLEについては、上流の第三者ライセンス表示を保持しています。.NETランタイムの[ライセンス](LICENSES/dotnet-runtime-LICENSE.txt)と[表示](LICENSES/dotnet-runtime-NOTICES.txt)も同梱しています。

## FFmpeg・ffprobe

FFmpegとffprobeは外部実行ファイルであり、本配布には含めていません。利用者が別途入手・管理してください。

FFmpegに適用されるライセンスは、そのビルド構成によって異なります。LTCueのMITライセンスはFFmpegには適用されません。

## テスト用メディア

`TestMedia/`には、生成したパターンと描画済みの文字を使用しています。フォントのバイナリは含めていません。

## Lucideアイコン

LucideアイコンのSVGパスを[src/LtcPlayer.Avalonia/Assets/Lucide/](https://github.com/Nirfuture/LTCue/tree/main/src/LtcPlayer.Avalonia/Assets/Lucide)に収録し、[LucideIcon.cs](https://github.com/Nirfuture/LTCue/blob/main/src/LtcPlayer.Avalonia/LucideIcon.cs)で描画しています。

ISCライセンスと、Feather由来のアイコンに関するMITライセンス表示は、[LICENSES/Lucide.txt](LICENSES/Lucide.txt)に収録しています。このファイルは実行用ZIPにも同梱しています。詳細は[docs/metadata/asset-licenses.json](docs/metadata/asset-licenses.json)を参照してください。

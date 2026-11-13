# License scope

Application C# and native/phase5 are MIT, copyright 2026 LTCue contributors.
native/Audio.cpp and the libltc code statically included in AudioNative.dll are LGPL-3.0-or-later. See LICENSES/LGPL-3.0.txt and GPL-3.0.txt, third_party/libltc/COPYING and source headers.
libltc v1.3.2 commit bf84b01097a1789c0296cc5fcfc3bf4608407930, original source retained. AudioNative.dll may be replaced with an ABI-compatible modified build. The supplied source and native/build-phase2.cmd permit rebuilding/relinking the library.
NuGet licenses and notices are in LICENSES/NuGet and docs/package-licenses.json. Skia/HarfBuzz/ANGLE retain their upstream third-party notices. .NET runtime notices are in LICENSES.
FFmpeg and ffprobe are external executables, not distributed here. Users obtain and maintain them separately. FFmpeg license depends on its build; application MIT does not relicense FFmpeg.
TestMedia are generated patterns and rendered glyphs, no font binary is included.

Lucide icon SVG paths are vendored in src/LtcPlayer.Avalonia/Assets/Lucide and rendered by LucideIcon.cs. Their ISC license and the MIT notice for Feather-derived icons are reproduced in LICENSES/Lucide.txt. This notice and license are included in the binary package. See docs/asset-licenses.json.

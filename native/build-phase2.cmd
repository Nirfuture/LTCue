@echo off
setlocal
cd /d "%~dp0"
if not defined VSCMD_VER (
  for /f "usebackq tokens=*" %%i in (`"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do call "%%i\VC\Auxiliary\Build\vcvars64.bat"
)
if not defined VSCMD_VER exit /b 1
if not exist build mkdir build
cd build
cl /nologo /utf-8 /O2 /MD /c /TC /I"../../third_party/libltc/src" ../../third_party/libltc/src/ltc.c ../../third_party/libltc/src/decoder.c ../../third_party/libltc/src/encoder.c ../../third_party/libltc/src/timecode.c
if errorlevel 1 exit /b 1
cl /nologo /utf-8 /O2 /MD /EHsc /std:c++17 /LD /I"../../third_party/libltc/src" ../Audio.cpp ltc.obj decoder.obj encoder.obj timecode.obj /link /OUT:AudioNative.dll ole32.lib uuid.lib propsys.lib
exit /b %errorlevel%



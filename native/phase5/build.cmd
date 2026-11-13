@echo off
setlocal
cd /d "%~dp0"
if not defined VSCMD_VER (
 for /f "usebackq tokens=*" %%i in (`"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do call "%%i\VC\Auxiliary\Build\vcvars64.bat"
)
if not defined VSCMD_VER exit /b 1
if not exist build mkdir build
cd build
cl /nologo /utf-8 /O2 /MD /EHsc /std:c++17 /LD ../Preview.cpp ../PreviewAudio.cpp /link /OUT:PlayerNative.dll d3d11.lib dxgi.lib user32.lib gdi32.lib d3dcompiler.lib ole32.lib uuid.lib
exit /b %errorlevel%

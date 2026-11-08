@echo off
rem Builds Toneline EQ into out\ (developers and CI). Users run "Install Toneline EQ.cmd" instead.
setlocal enabledelayedexpansion
cd /d "%~dp0"
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FW%\csc.exe" (echo .NET Framework 4 C# compiler not found.& exit /b 1)
if not exist out mkdir out
set "REFS=/lib:"%FW%\WPF" /r:System.dll /r:System.Core.dll /r:System.Xaml.dll /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll"
set "NP="
set "WM=%WINDIR%\System32\WinMetadata"
if exist "%WM%\Windows.Media.winmd" (
  set "NP=/define:NOWPLAYING /r:%WM%\Windows.Foundation.winmd /r:%WM%\Windows.Media.winmd /r:%WM%\Windows.Storage.winmd"
  for %%a in (System.Runtime System.Runtime.WindowsRuntime System.Runtime.InteropServices.WindowsRuntime System.Threading.Tasks System.IO) do (
    for /f "delims=" %%f in ('dir /s /b "%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\%%a\%%a.dll" 2^>nul') do set "NP=!NP! /r:%%f"
  )
)
"%FW%\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /out:out\TonelineEQ.exe /win32icon:TonelineEQ.ico !REFS! !NP! src\*.cs
if not errorlevel 1 goto built
echo Building without Now Playing...
"%FW%\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /out:out\TonelineEQ.exe /win32icon:TonelineEQ.ico !REFS! src\*.cs
if errorlevel 1 exit /b 1
:built
xcopy /y /q /i fonts out\fonts >nul
echo Built out\TonelineEQ.exe

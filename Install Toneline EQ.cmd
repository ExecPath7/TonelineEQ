@echo off
setlocal enabledelayedexpansion
title Toneline EQ setup
cd /d "%~dp0"

set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FW%\csc.exe" (
  echo Could not find the .NET Framework 4 C# compiler on this PC.
  pause
  exit /b 1
)

set "OUT=%TEMP%\TonelineEQ-build"
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%"

rem "Now Playing" uses Windows' media APIs (Windows 10 1809 and later). If they're missing, Toneline EQ builds without it.
set "NP="
set "WM=%WINDIR%\System32\WinMetadata"
if exist "%WM%\Windows.Media.winmd" (
  set "NP=/define:NOWPLAYING /r:%WM%\Windows.Foundation.winmd /r:%WM%\Windows.Media.winmd /r:%WM%\Windows.Storage.winmd"
  for %%a in (System.Runtime System.Runtime.WindowsRuntime System.Runtime.InteropServices.WindowsRuntime System.Threading.Tasks System.IO) do (
    for /f "delims=" %%f in ('dir /s /b "%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\%%a\%%a.dll" 2^>nul') do set "NP=!NP! /r:%%f"
  )
)
echo Building Toneline EQ...
"%FW%\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /out:"%OUT%\TonelineEQ.exe" /win32icon:TonelineEQ.ico ^
  /lib:"%FW%\WPF" /r:System.dll /r:System.Core.dll /r:System.Xaml.dll /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  !NP! src\*.cs > "%OUT%\build.txt" 2>&1
if not errorlevel 1 goto built
rem Build again without Now Playing (older Windows), showing any errors.
"%FW%\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /out:"%OUT%\TonelineEQ.exe" /win32icon:TonelineEQ.ico ^
  /lib:"%FW%\WPF" /r:System.dll /r:System.Core.dll /r:System.Xaml.dll /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo Build failed. Copy the errors above into an issue on Toneline EQ's GitHub page.
  pause
  exit /b 1
)
:built

if exist fonts xcopy /y /q /i fonts "%OUT%\fonts" >nul
start "" "%OUT%\TonelineEQ.exe" --setup "%~dp0."

@echo off
setlocal EnableExtensions EnableDelayedExpansion

if "%~1"=="" (
  echo %~nx0(1^): error FB0001: Missing TargetDir argument.
  echo %~nx0(2^): error FB0002: Usage: %~nx0 "<TargetDir>" ["win-x64"^|"win-x86"^|"win-arm64"^|"linux-x64"]
  exit /b 1
)

set "TARGET_DIR=%~1"

rem Remove trailing backslash to avoid \" quote-escaping issues
if "%TARGET_DIR:~-1%"=="\" set "TARGET_DIR=%TARGET_DIR:~0,-1%"

set "RID_OVERRIDE=%~2"
set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%postbuild_onnx.ps1"

if not exist "%PS_SCRIPT%" (
  echo %~nx0(10^): error FB0003: PowerShell script not found: "%PS_SCRIPT%"
  exit /b 1
)

rem ==========================
rem VS-friendly prechecks here
rem ==========================

set "SRC_BASE=%SCRIPT_DIR%\..\FlowBloxResources\data\onnxruntimes"
for %%I in ("%SCRIPT_DIR%\..\FlowBloxResources\data\onnxruntimes") do set "SRC_BASE=%%~fI"

if not exist "%SRC_BASE%" (
  echo %~nx0(20^): error FB0100: Source folder not found: "%SRC_BASE%"
  echo %~nx0(21^): error FB0101: Please run "FlowBloxResources\Sync-OnnxRuntimes.ps1" before starting a build.
  echo %~nx0(22^): warning FB0199: If you are updating the OnnxRuntime.Managed NuGet library, be sure to run the script again.
  exit /b 1
)

set "SRC_BASE_GENAI=%SCRIPT_DIR%\..\FlowBloxResources\data\onnxruntimesgenai"
for %%I in ("%SCRIPT_DIR%\..\FlowBloxResources\data\onnxruntimesgenai") do set "SRC_BASE_GENAI=%%~fI"

if not exist "%SRC_BASE_GENAI%" (
  echo %~nx0(30^): error FB0110: Source folder not found: "%SRC_BASE_GENAI%"
  echo %~nx0(31^): error FB0111: Please run "FlowBloxResources\Sync-OnnxRuntimes.ps1" before starting a build.
  echo %~nx0(32^): warning FB0112: If you are updating the OnnxRuntimeGenAi.Managed NuGet library, be sure to run the script again.
  exit /b 1
)

if not exist "%TARGET_DIR%" (
  echo %~nx0(40^): error FB0200: TargetDir not found: "%TARGET_DIR%"
  exit /b 1
)

echo.
echo [FlowBlox] PostBuild (PowerShell)
echo   TargetDir : %TARGET_DIR%
if not "%RID_OVERRIDE%"=="" echo   RID       : %RID_OVERRIDE%
echo.

rem Build common PowerShell args safely
set "COMMON_ARGS=-NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" -TargetDir "%TARGET_DIR%""
if not "%RID_OVERRIDE%"=="" set "COMMON_ARGS=%COMMON_ARGS% -Rid "%RID_OVERRIDE%""

rem Copy ONNX Runtime
powershell %COMMON_ARGS% -DataSubfolder onnxruntimes
if errorlevel 1 (
  echo %~nx0(70^): error FB0300: PowerShell postbuild failed for onnxruntimes.
  exit /b 1
)

rem Copy ONNX Runtime GenAI
powershell %COMMON_ARGS% -DataSubfolder onnxruntimesgenai
if errorlevel 1 (
  echo %~nx0(80^): error FB0310: PowerShell postbuild failed for onnxruntimesgenai.
  exit /b 1
)

echo.
echo [FlowBlox] PostBuild done.
exit /b 0

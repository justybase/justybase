@echo off
setlocal
if "%~1"=="" (
  echo Usage: publishWindows.bat ^<version^> [aot-netezza^|self-contained-netezza-db2]
  exit /b 1
)
if "%~2"=="" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\package-windows.ps1" -Version "%~1" -OutputRoot "artifacts"
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\package-windows.ps1" -Version "%~1" -OutputRoot "artifacts" -Variant "%~2"
)
exit /b %ERRORLEVEL%

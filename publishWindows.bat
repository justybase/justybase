@echo off
setlocal
if "%~1"=="" (
  echo Usage: publishWindows.bat ^<version^>
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\package-windows.ps1" -Version "%~1" -OutputRoot "artifacts"
exit /b %ERRORLEVEL%

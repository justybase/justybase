@echo off
setlocal
if "%~1"=="" (
  echo Usage: publishWindows.bat ^<version^>
  exit /b 1
)
set "packageMode=1"
:selectMode
echo.
echo Select installer variant:
echo   1^) Full - all default database drivers [default]
echo   2^) Light - without DuckDB, Access, Oracle, or PostgreSQL
set /p "packageMode=Choice [1]: "
if "%packageMode%"=="" set "packageMode=1"
if "%packageMode%"=="1" goto fullMode
if "%packageMode%"=="2" goto lightMode
echo Invalid choice. Enter 1 or 2.
set "packageMode="
goto selectMode

:fullMode
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\package-windows.ps1" -Version "%~1" -OutputRoot "artifacts"
exit /b %ERRORLEVEL%

:lightMode
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\package-windows.ps1" -Version "%~1" -OutputRoot "artifacts" -Light
exit /b %ERRORLEVEL%

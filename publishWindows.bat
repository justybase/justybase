@echo off
setlocal
set "version=%~1"
if not defined version goto promptVersion
goto versionReady

:promptVersion
set /p "version=Enter version to publish: "
if not defined version (
  echo Version number is required.
  goto promptVersion
)

:versionReady
set "packageMode=1"
:selectMode
echo.
echo Select installer variant:
echo   1^) Full - all database drivers + AI [default]
echo   2^) Slim - Netezza + SQLite + DB2 only, no AI
set /p "packageMode=Choice [1]: "
if "%packageMode%"=="" set "packageMode=1"
if "%packageMode%"=="1" goto fullMode
if "%packageMode%"=="2" goto slimMode
echo Invalid choice. Enter 1 or 2.
set "packageMode="
goto selectMode

:fullMode
set "variant=aot-self-contained"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\package-windows.ps1" -Version "%version%" -OutputRoot "artifacts"
set "publishExit=%ERRORLEVEL%"
goto finish

:slimMode
set "variant=aot-self-contained-slim"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\package-windows.ps1" -Version "%version%" -OutputRoot "artifacts" -Slim -NoVelopack
set "publishExit=%ERRORLEVEL%"
goto finish

:finish
echo.
if "%publishExit%"=="0" (
  echo Publish completed successfully.
  if exist "artifacts\JustyBase-%version%-win-x64-%variant%-Setup.exe" echo Installer: artifacts\JustyBase-%version%-win-x64-%variant%-Setup.exe
  echo Archive:   artifacts\JustyBase-%version%-win-x64-%variant%.zip
) else (
  echo Publish failed. Exit code: %publishExit%
  echo Output directory: artifacts\
)
echo.
pause
exit /b %publishExit%

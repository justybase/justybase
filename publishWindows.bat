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
echo   1^) Full - all default database drivers [default]
echo   2^) Light - without DuckDB / Excel File SQL, Access, Oracle, or PostgreSQL
set /p "packageMode=Choice [1]: "
if "%packageMode%"=="" set "packageMode=1"
if "%packageMode%"=="1" goto fullMode
if "%packageMode%"=="2" goto lightMode
echo Invalid choice. Enter 1 or 2.
set "packageMode="
goto selectMode

:fullMode
set "variant=aot-self-contained"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\package-windows.ps1" -Version "%version%" -OutputRoot "artifacts"
set "publishExit=%ERRORLEVEL%"
goto finish

:lightMode
set "variant=aot-self-contained-light"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\package-windows.ps1" -Version "%version%" -OutputRoot "artifacts" -Light
set "publishExit=%ERRORLEVEL%"
goto finish

:finish
echo.
if "%publishExit%"=="0" (
  echo Publish completed successfully.
  echo Installer: artifacts\JustyBase-%version%-win-x64-%variant%-Setup.exe
  echo Archive:   artifacts\JustyBase-%version%-win-x64-%variant%.zip
) else (
  echo Publish failed. Exit code: %publishExit%
  echo Output directory: artifacts\
)
echo.
pause
exit /b %publishExit%

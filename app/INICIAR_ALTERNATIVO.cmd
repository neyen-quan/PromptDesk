@echo off
cd /d "%~dp0"
if exist "PromptDesk.exe" goto launch
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Preparar.ps1"
if errorlevel 1 goto failure
:launch
start "" "%~dp0PromptDesk.exe"
exit /b
:failure
echo No se pudo preparar PromptDesk. Compartime compilacion.log y pruebas-error.txt si existe.
pause

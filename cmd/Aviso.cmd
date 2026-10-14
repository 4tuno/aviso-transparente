@echo off
setlocal
set "avisoMode=%~1"
if not defined avisoMode set "avisoMode=ocultar"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Aviso.ps1" -Accion "%avisoMode%"
set "avisoResult=%errorlevel%"
if "%~1"=="" pause
exit /b %avisoResult%

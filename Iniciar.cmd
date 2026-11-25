@echo off
setlocal
title Iniciar Ztrx
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\play.ps1" %*
set "ZTRX_EXIT_CODE=%ERRORLEVEL%"
echo.
if not "%ZTRX_EXIT_CODE%"=="0" echo O iniciador encerrou com erro. Consulte a mensagem acima e a pasta logs.
if not defined ZTRX_NO_PAUSE pause
exit /b %ZTRX_EXIT_CODE%

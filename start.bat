@echo off
setlocal
title LCET - LiveCaption Edge Translator
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -STA -File "%~dp0launcher\Start-LCET.ps1"
if errorlevel 1 (
  echo.
  echo LCET could not start. See the error above. Contact IT if scripts or DLLs are blocked.
  pause
  exit /b 1
)
endlocal
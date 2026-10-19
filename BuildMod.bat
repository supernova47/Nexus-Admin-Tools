@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0BuildMod.ps1"
if errorlevel 1 (
  echo.
  echo Build failed. Read the error messages above.
  pause
  exit /b 1
)
echo.
echo Build complete.
pause

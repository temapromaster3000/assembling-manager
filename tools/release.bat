@echo off
chcp 65001 >nul
echo ==========================================
echo Assembling Manager - Release
echo ==========================================
echo.
echo This script builds all Revit versions, packs ZIPs,
echo builds the installer and publishes a GitHub Release.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" %*
echo.
pause

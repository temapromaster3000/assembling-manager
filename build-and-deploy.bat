@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ==========================================
echo Assembling Manager - Build and Deploy
echo ==========================================
echo.

set "SOLUTION=build\AssemblingManager.sln"
set "ADDIN_NAME=AssemblingManager.addin"
set "DLL_NAME=AssemblingManager.dll"
set "PLUGIN_DIR=%APPDATA%\AssemblingManager"

if not exist "%SOLUTION%" (
    echo [ERROR] Solution not found: %SOLUTION%
    pause
    exit /b 1
)

echo [1/6] Building Revit 2021 (Debug.R21, net48)...
dotnet build "%SOLUTION%" -c Debug.R21 --verbosity quiet
if errorlevel 1 ( echo [ERROR] Build R21 failed! & pause & exit /b 1 )
echo [OK] Revit 2021 build successful
echo.

echo [2/6] Building Revit 2022 (Debug.R22, net48)...
dotnet build "%SOLUTION%" -c Debug.R22 --verbosity quiet
if errorlevel 1 ( echo [ERROR] Build R22 failed! & pause & exit /b 1 )
echo [OK] Revit 2022 build successful
echo.

echo [3/6] Building Revit 2023 (Debug.R23, net48)...
dotnet build "%SOLUTION%" -c Debug.R23 --verbosity quiet
if errorlevel 1 ( echo [ERROR] Build R23 failed! & pause & exit /b 1 )
echo [OK] Revit 2023 build successful
echo.

echo [4/6] Building Revit 2024 (Debug.R24, net48)...
dotnet build "%SOLUTION%" -c Debug.R24 --verbosity quiet
if errorlevel 1 ( echo [ERROR] Build R24 failed! & pause & exit /b 1 )
echo [OK] Revit 2024 build successful
echo.

echo [5/6] Building Revit 2025 (Debug.R25, net8.0-windows)...
dotnet build "%SOLUTION%" -c Debug.R25 --verbosity quiet
if errorlevel 1 ( echo [ERROR] Build R25 failed! & pause & exit /b 1 )
echo [OK] Revit 2025 build successful
echo.

echo [6/6] Deploying to installed Revit versions...

if not exist "%PLUGIN_DIR%" mkdir "%PLUGIN_DIR%"

call :DeployVersion 2021 R21 net48
call :DeployVersion 2022 R22 net48
call :DeployVersion 2023 R23 net48
call :DeployVersion 2024 R24 net48
call :DeployVersion 2025 R25 net8.0-windows

echo.
echo ==========================================
echo [SUCCESS] Build and deploy completed
echo ==========================================
echo.
echo Plugin files: %PLUGIN_DIR%
echo Addin manifests: %%APPDATA%%\Autodesk\Revit\Addins\{Year}
echo.
pause
exit /b 0

:DeployVersion
set "YEAR=%~1"
set "CONFIG=%~2"
set "TFM=%~3"
set "SOURCE_DIR=bin\Debug.%CONFIG%\%YEAR%"
set "TARGET_DIR=%PLUGIN_DIR%\%YEAR%"
set "ADDIN_DIR=%APPDATA%\Autodesk\Revit\Addins\%YEAR%"

if not exist "%ADDIN_DIR%" (
    echo [SKIP] Revit %YEAR% not installed
    goto :eof
)

if not exist "%SOURCE_DIR%\%DLL_NAME%" (
    echo [SKIP] Build output not found for Revit %YEAR%: %SOURCE_DIR%
    goto :eof
)

if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"

copy /Y "%SOURCE_DIR%\*.dll" "%TARGET_DIR%\" >nul
copy /Y "%SOURCE_DIR%\*.pdb" "%TARGET_DIR%\" >nul 2>nul

call :WriteAddin "%ADDIN_DIR%\%ADDIN_NAME%" "%TARGET_DIR%\%DLL_NAME%"

echo [OK] Revit %YEAR% deployed to %TARGET_DIR%
goto :eof

:WriteAddin
echo ^<?xml version="1.0" encoding="utf-8"?^> > "%~1"
echo ^<RevitAddIns^> >> "%~1"
echo ^<AddIn Type="Application"^> >> "%~1"
echo ^<Name^>Assembling Manager^</Name^> >> "%~1"
echo ^<Assembly^>%~2^</Assembly^> >> "%~1"
echo ^<AddInId^>01958C2F-6E03-4812-AEC1-B3362506B1CC^</AddInId^> >> "%~1"
echo ^<FullClassName^>AssemblingManager.Revit.App^</FullClassName^> >> "%~1"
echo ^<VendorId^>YOURCOMPANY^</VendorId^> >> "%~1"
echo ^<VendorDescription^>Your company description^</VendorDescription^> >> "%~1"
echo ^</AddIn^> >> "%~1"
echo ^</RevitAddIns^> >> "%~1"
goto :eof

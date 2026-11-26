@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

rem VS Code C# Dev Kit injects these variables into its terminal; they break dotnet build.
set "CustomAfterMicrosoftCommonTargets="
set "CustomAfterMicrosoftCommonCrossTargetingTargets="
set "EnableProjectDataOnBuild=false"
set "EnableProjectDataInProjectFolder=false"

echo ==========================================
echo Assembling Manager - Build and Deploy
echo ==========================================
echo.

set "SOLUTION=build\AssemblingManager.sln"
set "ADDIN_NAME=AssemblingManager.addin"
set "DLL_NAME=AssemblingManager.dll"
set "PLUGIN_DIR=%APPDATA%\AssemblingManager"
set "MCP_PROJECT=src\AssemblingManager.McpServer\AssemblingManager.McpServer.csproj"
set "MCP_STAGING=bin\Debug\McpServer"
set "MCP_TARGET=%PLUGIN_DIR%\McpServer"

if not exist "%SOLUTION%" (
    echo [ERROR] Solution not found: %SOLUTION%
    pause
    exit /b 1
)

echo [1/7] Building Revit 2021 (Debug.R21, net48)...
dotnet build "%SOLUTION%" -c Debug.R21 --verbosity quiet
if errorlevel 1 ( echo [ERROR] Build R21 failed! & pause & exit /b 1 )
echo [OK] Revit 2021 build successful
echo.

echo [2/7] Building Revit 2022 (Debug.R22, net48)...
dotnet build "%SOLUTION%" -c Debug.R22 --verbosity quiet
if errorlevel 1 ( echo [ERROR] Build R22 failed! & pause & exit /b 1 )
echo [OK] Revit 2022 build successful
echo.

echo [3/7] Building Revit 2023 (Debug.R23, net48)...
dotnet build "%SOLUTION%" -c Debug.R23 --verbosity quiet
if errorlevel 1 ( echo [ERROR] Build R23 failed! & pause & exit /b 1 )
echo [OK] Revit 2023 build successful
echo.

echo [4/7] Building Revit 2024 (Debug.R24, net48)...
dotnet build "%SOLUTION%" -c Debug.R24 --verbosity quiet
if errorlevel 1 ( echo [ERROR] Build R24 failed! & pause & exit /b 1 )
echo [OK] Revit 2024 build successful
echo.

echo [5/7] Building Revit 2025 (Debug.R25, net8.0-windows)...
dotnet build "%SOLUTION%" -c Debug.R25 --verbosity quiet
if errorlevel 1 ( echo [ERROR] Build R25 failed! & pause & exit /b 1 )
echo [OK] Revit 2025 build successful
echo.

echo [6/7] Building MCP server (net8.0)...
if exist "%MCP_STAGING%" rmdir /S /Q "%MCP_STAGING%"
dotnet build "%MCP_PROJECT%" -c Debug -o "%MCP_STAGING%" --verbosity quiet
if errorlevel 1 ( echo [ERROR] MCP server build failed! & pause & exit /b 1 )
echo [OK] MCP server build successful
echo.

echo [7/7] Deploying to installed Revit versions...

if not exist "%PLUGIN_DIR%" mkdir "%PLUGIN_DIR%"

call :DeployVersion 2021 R21 net48
call :DeployVersion 2022 R22 net48
call :DeployVersion 2023 R23 net48
call :DeployVersion 2024 R24 net48
call :DeployVersion 2025 R25 net8.0-windows
call :DeployMcpServer

echo.
echo ==========================================
echo [SUCCESS] Build and deploy completed
echo ==========================================
echo.
echo Plugin files: %PLUGIN_DIR%
echo MCP server: %MCP_TARGET%
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

:DeployMcpServer
if not exist "%MCP_STAGING%\AssemblingManager.McpServer.exe" (
    echo [SKIP] MCP server build output not found: %MCP_STAGING%
    goto :eof
)

if not exist "%MCP_TARGET%" mkdir "%MCP_TARGET%"

rem robocopy returns 0-7 on success; 8+ means files are locked or copy failed.
robocopy "%MCP_STAGING%" "%MCP_TARGET%" /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 (
    echo [WARN] Could not update MCP server in %MCP_TARGET%.
    echo        Close opencode and AssemblingManager.McpServer.exe, then rerun this script.
) else (
    echo [OK] MCP server deployed to %MCP_TARGET%
)
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

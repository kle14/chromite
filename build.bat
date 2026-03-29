@echo off
setlocal enabledelayedexpansion

echo.
echo ============================================================
echo   SecureBrowser - Build and Run
echo ============================================================
echo.

:: ── Check .NET 8 SDK ─────────────────────────────────────────────────────
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo  [ERROR] .NET SDK not found on this machine.
    echo.
    echo  Please install the .NET 8 SDK (it's free):
    echo  https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    echo  → Download ".NET 8.0 SDK" for Windows x64
    echo  → Run the installer
    echo  → Come back and double-click build.bat again
    echo.
    pause
    exit /b 1
)

for /f "tokens=*" %%v in ('dotnet --version 2^>nul') do set DOTNET_VER=%%v
echo  [✓] .NET SDK found: %DOTNET_VER%

:: ── Restore NuGet packages ────────────────────────────────────────────────
echo  [*] Restoring NuGet packages (WebView2)...
dotnet restore SecureBrowser.csproj --verbosity quiet
if errorlevel 1 (
    echo.
    echo  [ERROR] Failed to restore packages.
    echo  Make sure you have an internet connection on first run.
    echo.
    pause
    exit /b 1
)
echo  [✓] Packages restored.

:: ── Build ────────────────────────────────────────────────────────────────
echo  [*] Building SecureBrowser...
dotnet build SecureBrowser.csproj -c Release --verbosity quiet --no-restore
if errorlevel 1 (
    echo.
    echo  [ERROR] Build failed. Full error output:
    echo.
    dotnet build SecureBrowser.csproj -c Release
    echo.
    pause
    exit /b 1
)
echo  [✓] Build successful.

:: ── Launch ───────────────────────────────────────────────────────────────
echo  [*] Launching SecureBrowser...
echo.
dotnet run --project SecureBrowser.csproj -c Release --no-build

echo.
echo  SecureBrowser exited.
pause

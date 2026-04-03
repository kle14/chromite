@echo off
echo ============================================
echo   Secure Browser v2.0 — Setup
echo ============================================
echo.

REM ── Step 1: Start PostgreSQL ──────────────────
echo [1/3] Starting PostgreSQL container...
docker-compose up -d
if errorlevel 1 (
    echo.
    echo ERROR: Docker failed. Make sure Docker Desktop is running.
    echo.
    pause
    exit /b 1
)

echo      Waiting 5 seconds for PostgreSQL to initialize...
timeout /t 5 /nobreak >nul

REM ── Step 2: Restore packages ──────────────────
echo [2/3] Restoring NuGet packages...
dotnet restore SecureBrowser.csproj
if errorlevel 1 (
    echo.
    echo ERROR: dotnet restore failed. Make sure .NET 8 SDK is installed.
    pause
    exit /b 1
)

REM ── Step 3: Build and run ─────────────────────
echo [3/3] Building and launching...
echo.
dotnet run --project SecureBrowser.csproj -c Release
pause

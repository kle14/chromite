@echo off
echo Starting PostgreSQL container (if not running)...
docker-compose up -d 2>nul
timeout /t 2 /nobreak >nul
echo Launching Secure Browser...
dotnet run --project SecureBrowser.csproj -c Release
pause

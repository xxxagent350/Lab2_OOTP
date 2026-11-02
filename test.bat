@echo off
cd /d "%~dp0"
dotnet run --project tests\Lab2.Engine.Tests\Lab2.Engine.Tests.csproj
pause

@echo off
rem Builds the self-contained VipShop folder for the VM (Windows x64, no .NET to install there):
rem   publish\VipShop.exe + appsettings.json + wwwroot\ + Start.cmd
cd /d "%~dp0"
dotnet publish src\VipShop\VipShop.csproj -c Release -o publish || exit /b 1
del /q publish\*.pdb 2>nul
echo VipShop published to %~dp0publish

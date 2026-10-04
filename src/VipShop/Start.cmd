@echo off
rem VipShop: VIP package web for the Trickster TrickMod server. Settings: appsettings.json (port, SQL Server, admin password).
rem Log: logs\vipshop-YYYYMMDD.log. Close this window to stop the site.
cd /d "%~dp0"
title VipShop
VipShop.exe
if errorlevel 1 pause

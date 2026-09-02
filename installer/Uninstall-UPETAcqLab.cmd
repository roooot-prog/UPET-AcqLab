@echo off
title Dezinstalare UPET AcqLab
cd /d "%~dp0"
echo.
echo  UPET AcqLab — dezinstalare (necesita Administrator)
echo.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0Uninstall-UPETAcqLab.ps1\"'"
echo.
echo  Daca a aparut UAC, confirmati.
pause

@echo off
title Diagnostic Spider8 USB — UPET AcqLab
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0Diagnose-Spider8Usb.ps1\"'"
pause

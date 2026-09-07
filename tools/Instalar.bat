@echo off
rem Doble clic para instalar el add-in. No hace falta abrir ninguna consola:
rem esto abre PowerShell por dentro con los permisos justos para esta
rem instalacion nada mas (no toca la configuracion general de PowerShell del
rem equipo).
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Instalar.ps1"

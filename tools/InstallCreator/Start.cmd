@echo off
REM Bugfix 11.08.2026: Desktop-Verknuepfung auf DIESE Datei zeigen lassen statt auf
REM HaelpMi.InstallCreator.exe direkt - Start.ps1 daneben prueft/erneuert die exe bei
REM Bedarf automatisch, bevor sie startet (siehe Kommentar dort).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start.ps1"

@echo off
rem Double-click to build. ASCII-only on purpose: cmd.exe reads batch files
rem byte-wise, and non-ASCII text desyncs its line offsets.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
pause

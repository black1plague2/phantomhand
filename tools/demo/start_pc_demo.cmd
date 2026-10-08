@echo off
REM Thin wrapper so the demo can be double-clicked / run from cmd.exe without
REM remembering PowerShell's execution-policy incantation. Forwards any
REM arguments straight through (e.g. start_pc_demo.cmd -AutoDrive -WithHaptics).
setlocal
set SCRIPT_DIR=%~dp0
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%start_pc_demo.ps1" %*
endlocal

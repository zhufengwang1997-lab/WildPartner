@echo off
rem Shared setup: results\<GPU>_<computer>_<time>\  (two test PCs never overwrite each other)
chcp 65001 >nul
set ROOT=%~dp0
set TS=
set GPU=
for /f "usebackq delims=" %%i in (`powershell -NoProfile -Command "Get-Date -Format yyyyMMdd-HHmm"`) do set TS=%%i
for /f "usebackq delims=" %%i in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%_gpu.ps1"`) do set GPU=%%i
if "%TS%"=="" set TS=run
if "%GPU%"=="" set GPU=GPU
set OUT=%ROOT%results\%GPU%_%COMPUTERNAME%_%TS%
mkdir "%OUT%" 2>nul
set EXE=%ROOT%WildPartner\WildPartner.exe
set COMMON=-force-d3d12 -screen-fullscreen 0 -caps-out "%OUT%"
powershell -NoProfile -Command "Get-CimInstance Win32_VideoController | Format-List Name,DriverVersion,AdapterRAM,VideoModeDescription; Get-CimInstance Win32_Processor | Format-List Name; Get-CimInstance Win32_OperatingSystem | Format-List Caption,Version" > "%OUT%\device_info.txt" 2>&1

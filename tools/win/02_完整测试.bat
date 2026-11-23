@echo off
call "%~dp0_env.bat"
rem Each step logs and continues even if the previous one failed (one chance per machine).
echo [1/5] 冒烟测试（约 5 分钟）...
"%EXE%" %COMMON% -smoketest -smokeout="%OUT%\smoke" -screen-width 1600 -screen-height 900 -logFile "%OUT%\smoke_player.log"
echo [2/5] 超分评估：所有后端 x 所有档位 + 原生 + 参考图（约 30 分钟）...
mkdir "%OUT%\bench" 2>nul
"%EXE%" %COMMON% -bench -capture="%OUT%\bench" -screen-width 1920 -screen-height 1080 -logFile "%OUT%\bench\player.log"
echo [3/5] GRD 关 / 开对比（约 4 分钟）...
mkdir "%OUT%\grd" 2>nul
"%EXE%" %COMMON% -grdbench -demoout="%OUT%\grd" -grd=off -screen-width 1920 -screen-height 1080 -logFile "%OUT%\grd\player_off.log"
"%EXE%" %COMMON% -grdbench -demoout="%OUT%\grd" -grd=on -screen-width 1920 -screen-height 1080 -logFile "%OUT%\grd\player_on.log"
echo [4/5] 特效开销（约 8 分钟）...
mkdir "%OUT%\fxcost" 2>nul
"%EXE%" %COMMON% -fxcost -demoout="%OUT%\fxcost" -screen-width 1920 -screen-height 1080 -logFile "%OUT%\fxcost\player.log"
echo [5/5] 挂机 10 分钟（TC-10）...
mkdir "%OUT%\soak" 2>nul
"%EXE%" %COMMON% -soak=600 -demoout="%OUT%\soak" -screen-width 1920 -screen-height 1080 -logFile "%OUT%\soak\player.log"
copy "%USERPROFILE%\AppData\LocalLow\zfw\WildPartner\*.json" "%OUT%\" >nul 2>&1
echo.
echo 全部完成。请把这个文件夹整个拷回 U 盘：
echo %OUT%
pause

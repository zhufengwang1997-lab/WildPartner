@echo off
call "%~dp0_env.bat"
echo 挂机 10 分钟（TC-10），请不要操作...
mkdir "%OUT%\soak" 2>nul
"%EXE%" %COMMON% -soak=600 -demoout="%OUT%\soak" -screen-width 1920 -screen-height 1080 -logFile "%OUT%\soak\player.log"
echo 完成。请把这个文件夹整个拷回 U 盘：
echo %OUT%
pause

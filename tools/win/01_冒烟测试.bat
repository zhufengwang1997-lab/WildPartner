@echo off
call "%~dp0_env.bat"
echo [1/1] 冒烟测试（约 5 分钟，窗口会自己动，请不要操作）...
"%EXE%" %COMMON% -smoketest -smokeout="%OUT%\smoke" -screen-width 1600 -screen-height 900 -logFile "%OUT%\smoke_player.log"
echo.
echo 完成。请把这个文件夹整个拷回 U 盘：
echo %OUT%
pause

@echo off
setlocal
set "APP=%~dp0artifacts\release\PCWatch\PCWatch.exe"
if exist "%APP%" (
    start "" "%APP%"
    exit /b 0
)
echo PCWatch: первый запуск, выполняется сборка...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if errorlevel 1 (
    echo.
    echo Сборка не выполнилась. Подробности выше.
    pause
    exit /b 1
)
if exist "%APP%" start "" "%APP%"

@echo off
cd /d "%~dp0"
call "%~dp0gradlew.bat" installClient --console=plain
pause

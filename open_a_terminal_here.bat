@echo off
:: 在当前脚本所在目录打开命令提示符（"Open Terminal Here"）
:: 若检测到本地 venv 则一并激活，方便直接运行 python main.py
cd /d "%~dp0" || exit /b 1
if exist "%~dp0venv\Scripts\activate.bat" (
    call "%~dp0venv\Scripts\activate.bat"
)
cmd /k

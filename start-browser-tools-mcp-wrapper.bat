@echo off
chcp 65001 >nul
set "PATH=C:\Program Files\nodejs;%PATH%"
cd /d "%~dp0"
"C:\Program Files\nodejs\npx.cmd" -y @agentdeskai/browser-tools-mcp@latest


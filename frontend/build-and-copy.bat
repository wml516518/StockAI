@echo off
echo Building Vue 3 frontend for production...
echo.
cd /d %~dp0
npm run build
echo.
echo Build complete! Files are in ../src/StockAnalyse.Api/wwwroot
echo.
pause


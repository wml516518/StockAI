@echo off
echo Starting Vue 3 frontend development server...
echo.
echo Make sure the backend API is running on http://localhost:5000
echo.
cd /d %~dp0
npm run dev
pause


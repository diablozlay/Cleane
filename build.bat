@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
cd /d "%~dp0"
set OUT=Cleaner_BETA_3.exe

rem 收集 icons 內所有 png 作為內嵌資源（邏輯名稱 = 檔名，程式以 name + ".png" 讀取）
set RES=
for %%f in (icons\*.png) do set RES=!RES! "-resource:icons\%%~nxf,%%~nxf"
rem 內嵌使用說明 PDF（readme 資料夾，邏輯名稱 = 檔名）
for %%f in (readme\*.pdf) do set RES=!RES! "-resource:readme\%%~nxf,%%~nxf"

where mcs >nul 2>nul
if %errorlevel%==0 (
  echo [使用 mono mcs]
  mcs -sdk:4.5 -target:winexe -platform:x64 -codepage:utf8 -optimize+ -r:System.Windows.Forms.dll -r:System.Drawing.dll -win32icon:app.ico -win32manifest:app.manifest !RES! "-out:%OUT%" Cleaner.cs I18n.cs
) else (
  set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
  if not exist "!CSC!" ( echo 找不到 mcs 或 csc，請安裝 Mono 或 .NET Framework 4.x & pause & exit /b 1 )
  echo [使用 .NET Framework csc]
  "!CSC!" /nologo /target:winexe /platform:x64 /codepage:65001 /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /win32icon:app.ico /win32manifest:app.manifest !RES! /out:%OUT% Cleaner.cs I18n.cs
)
if errorlevel 1 ( echo 編譯失敗 & pause & exit /b 1 )

rem 組出發佈資料夾
if exist release rmdir /s /q release
mkdir release
copy /y "%OUT%" release\ >nul
echo.
echo 完成：%CD%\release\%OUT%
pause

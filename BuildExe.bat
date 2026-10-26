@echo off
setlocal
cd /d "%~dp0"

set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" goto :nocsc

echo Building AA_BlueArchiveKoshoSearch.exe ...
set "ICON="
if exist app.ico set "ICON=/win32icon:app.ico"
"%CSC%" /nologo /target:winexe /optimize+ %ICON% /out:AA_BlueArchiveKoshoSearch.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll AA_BlueArchiveKoshoSearch.cs
if errorlevel 1 goto :fail

echo.
echo [OK] AA_BlueArchiveKoshoSearch.exe has been created in this folder.
echo You can copy the exe anywhere and run it by itself.
pause
exit /b 0

:nocsc
echo [ERROR] C# compiler csc.exe was not found.
echo         .NET Framework 4.x is required. It is preinstalled on Windows 10/11.
pause
exit /b 1

:fail
echo [ERROR] Build failed. Check the compiler messages above.
pause
exit /b 1

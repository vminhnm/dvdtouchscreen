@echo off
rem Rebuilds dvdtouchscreen.exe with the C# compiler that ships with Windows (no SDK needed).
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /out:dvdtouchscreen.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll *.cs
if errorlevel 1 (echo Build failed) else (echo Built dvdtouchscreen.exe)
pause

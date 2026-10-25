@echo off
setlocal
set "ROOT=%~dp0"
set "CB=%ProgramFiles%\CamBam plus 1.0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo ERROR: .NET Framework 4 x64 C# compiler was not found: "%CSC%"
  pause
  exit /b 1
)
if not exist "%CB%\CamBam.CAD.dll" (
  echo ERROR: CamBam 1.0 was not found at "%CB%"
  pause
  exit /b 1
)
if not exist "%CB%\plugins\ThreadingMOP.dll" (
  echo ERROR: ThreadingMOP.dll was not found in "%CB%\plugins"
  pause
  exit /b 1
)
"%CSC%" /nologo /target:library /platform:x64 /out:"%ROOT%Tapping.dll" /reference:"%CB%\CamBam.CAD.dll" /reference:"%CB%\CamBam.Geom.dll" /reference:"%CB%\plugins\ThreadingMOP.dll" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll "%ROOT%Tapping.cs"
if errorlevel 1 (
  echo BUILD FAILED
  pause
  exit /b 1
)
echo Build succeeded: "%ROOT%Tapping.dll"
pause

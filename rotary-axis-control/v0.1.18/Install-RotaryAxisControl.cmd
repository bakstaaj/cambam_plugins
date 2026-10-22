@echo off
setlocal
set "CAMBAM_HOME=%ProgramFiles%\CamBam plus 1.0"
if not exist "%CAMBAM_HOME%\plugins" (
  echo CamBam plugin folder not found: "%CAMBAM_HOME%\plugins"
  echo Edit this script to point CAMBAM_HOME to your CamBam installation folder.
  pause
  exit /b 1
)
copy /Y "%~dp0CamBam.CAD.Solids.dll" "%CAMBAM_HOME%\plugins\CamBam.CAD.Solids.dll"
if errorlevel 1 exit /b 1
copy /Y "%~dp0RotaryAxisCamBamPlugin.dll" "%CAMBAM_HOME%\plugins\RotaryAxisCamBamPlugin.dll"
if errorlevel 1 exit /b 1
if exist "%CAMBAM_HOME%\post" copy /Y "%~dp0RotaryAxisControl.cbpp" "%CAMBAM_HOME%\post\RotaryAxisControl.cbpp"
if not exist "%CAMBAM_HOME%\post" echo Copy RotaryAxisControl.cbpp into your active CamBam post folder.
echo Restart CamBam to load Rotary Axis Control.
pause

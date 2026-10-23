@echo off
setlocal
pushd "%~dp0source"
REM Copy CamBam.CAD.dll and CamBam.Geom.dll from your CamBam installation into source\CamBamLibs first.
msbuild Plugins.sln /t:Rebuild /p:Configuration=Release
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%

@echo off

cd "%~dp0"

dotnet publish .\BCSessionSync.csproj -c Debug -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false -o ./publish/win-x64
if %ERRORLEVEL% NEQ 0 pause & exit /B

if exist "%UserProfile%\Bin" (
  copy .\publish\win-x64\* "%UserProfile%\Bin\"
  if %ERRORLEVEL% NEQ 0 pause
)

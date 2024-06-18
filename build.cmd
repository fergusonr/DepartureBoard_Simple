echo off

:: set https_proxy=http://username:password@host.com:8080

git pull

if %ERRORLEVEL% NEQ 0 goto end /B %ERRORLEVEL%

dotnet build -c Release
dotnet build -c DebugTest

changelogformatter --md bin\Release\net8.0\changelog.md

:end

pause

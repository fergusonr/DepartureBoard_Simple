echo off

:: set https_proxy=http://username:password@host.com:8080

git pull

if %ERRORLEVEL% NEQ 0 goto end /B %ERRORLEVEL%

dotnet build -c Release
dotnet build -c DebugTest

git log --pretty=format:"%%cd %%d %%s" --date=format:"%%d/%%m/%%y" > bin\Release\net6.0\changelog.txt

:end

pause

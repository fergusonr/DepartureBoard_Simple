echo off

:: set https_proxy=http://username:password@host.com:8080

git pull

if %ERRORLEVEL% NEQ 0 goto end /B %ERRORLEVEL%

dotnet build -c Release
dotnet build -c DebugTest

git log --decorate-refs=none --pretty=format:"%%+D    %%cd %%s" --date=format:"%%d/%%m/%%y" --since=12/09/18 > bin\Release\netcoreapp3.1\changelog.txt

:end

pause

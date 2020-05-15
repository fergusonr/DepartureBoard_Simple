:: MSbuild wrapper

echo off

:: set https_proxy=http://username:password@host.com:8080

git pull

if %ERRORLEVEL% NEQ 0 goto end /B %ERRORLEVEL%

set EXE=
set EXE1="C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
set EXE2="C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
set EXE3="C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe"

set EXE4="C:\Program Files\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
set EXE5="C:\Program Files\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
set EXE6="C:\Program Files\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe"

FOR %%A IN (%EXE6% %EXE5% %EXE4% %EXE3% %EXE2% %EXE1%) DO (
  if exist %%A (
	set EXE=%%A	
  )
)

if %EXE%=="" (
  echo Could not find MSbuild
  goto end
)

%EXE% DepartureBoard.sln /m /warnaserror /p:Configuration=Release
%EXE% DepartureBoard.sln /m /warnaserror /p:Configuration=DebugTest


git log --decorate-refs=none --pretty=format:"%%+D    %%cd %%s" --date=format:"%%d/%%m/%%y" --since=12/09/18 > bin\Release\changelog.txt

:end

pause

:: @ECHO OFF

:: paths
SET curdir=%~dp0
SET projectdir=%curdir%

:: Does string have a trailing slash? if so remove it 
IF %projectdir:~-1%==\ SET projectdir=%projectdir:~0,-1% 
SET publishdir="%projectdir%\bin\release\publish"


:: publish 
del "%publishdir%" /s /q
dotnet "%projectdir%" publish -c "Release" --output "%publishdir%" --framework netcoreapp3.1 --runtime win-x64 --no-self-contained --version-suffix  aaa
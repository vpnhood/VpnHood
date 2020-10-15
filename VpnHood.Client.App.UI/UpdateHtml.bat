@ECHO OFF

:: bin paths
SET curdir=%~dp0
SET uiDir=%curdir%..\VpnHood.Client.App.UI.Html
SET distDir=%uiDir%\dist
SET htmlZip=%curdir%Html.zip

:: build output
cd "%uiDir%"
call npm run build

:: zip the output
cd "%distDir%"
tar.exe -a -c -f "%htmlZip%" *

cd "%curdir%"
pause
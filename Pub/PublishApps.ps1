param( 
	[Parameter(Mandatory=$true)][object]$bump,
	[Parameter(Mandatory=$true)][object]$nugets,
	[Parameter(Mandatory=$true)][object]$clientWin,
	[Parameter(Mandatory=$true)][object]$clientAndroid,
	[Parameter(Mandatory=$true)][object]$connectWin,
	[Parameter(Mandatory=$true)][object]$connectAndroid,
	[Parameter(Mandatory=$true)][object]$maui,
	[Parameter(Mandatory=$true)][object]$server,
	[Parameter(Mandatory=$true)][object]$distribute,
	[Parameter(Mandatory=$true)][object]$samples
	);

$nugets = $nugets -eq "1";
$connectWin = $connectWin -eq "1";
$connectAndroid = $connectAndroid -eq "1";
$clientWin = $clientWin -eq "1";
$clientAndroid = $clientAndroid -eq "1";
$distribute = $distribute -eq "1";
$server = $server -eq "1";
$samples = $samples -eq "1";
$maui = $maui -eq "1";

. "$PSScriptRoot/Core/Common.ps1" -bump $bump

# clean all
& $msbuild "$solutionDir" /p:Configuration=Release /t:Clean /verbosity:$msverbosity;
$noclean = $true;
$noPushNuget = !$nugets

Remove-Item "$packagesRootDir/ReleaseNote.txt" -ErrorAction Ignore;

# rebuild libraries
if ($nugets) {
	& "$solutionDir/Src/Core/VpnHood.Core.Common/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Tunneling/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Client/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Client.Device.Android/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Client.Device/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Client.Device.WinDivert/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Server/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Server.Access/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Server.Access.FileAccessManager/_publish.ps1";

	& "$solutionDir/Src/AppLib/VpnHood.AppLib.Abstractions/_publish.ps1";
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.App/_publish.ps1";
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.Resources/_publish.ps1";
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.WebServer/_publish.ps1";
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.Store/_publish.ps1";
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.Android.Common/_publish.ps1";
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.Android.GooglePlay/_publish.ps1";
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.Android.GooglePlay.Core/_publish.ps1";
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.Android.Ads.AdMob/_publish.ps1";
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.Win.Common/_publish.ps1";
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.Win.Common.WpfSpa/_publish.ps1";
}

# publish MAUI nugets
if ($maui) {
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.Maui.Common/_publish.ps1";
}

# publish win client
if ($connectWin) {
	& "$solutionDir/Src/Apps/Connect.Win.Web/_publish.ps1";
}

# publish win client
if ($clientWin) {
	& "$solutionDir/Src/Apps/Client.Win.Web/_publish.ps1";
}

# publish server
if ($server) {	
	& "$solutionDir/Src/Apps/Server.Net/Pub/publish_win.ps1";
	& "$solutionDir/Src/Apps/Server.Net/Pub/publish_linux_x64.ps1";
	& "$solutionDir/Src/Apps/Server.Net/Pub/publish_linux_arm64.ps1";
	& "$solutionDir/Src/Apps/Server.Net/Pub/publish_docker.ps1" -distribute $distribute;
}

# publish android
if ($clientAndroid) {	
	& "$solutionDir/Src/Apps/Client.Android.Google/_publish.ps1";
	& "$solutionDir/Src/Apps/Client.Android.Web/_publish.ps1";
}

# publish android
if ($connectAndroid) {	
	& "$solutionDir/Src/Apps/Connect.Android.Google/_publish.ps1";
	& "$solutionDir/Src/Apps/Connect.Android.Web/_publish.ps1";
}


# distribute
if ($distribute) {
    & "$PSScriptRoot/PublishToGitHub.ps1" `
		-mainRepo ($clientWin -or $clientAndroid -or $server) `
		-connectRepo ($connectWin -or $connectAndroid);
}


# update and push samples nugets
if ($samples) {
	& "$solutionDir/../VpnHood.App.Samples/UpdateAndPush.ps1";
}

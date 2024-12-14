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
	& "$solutionDir/Core/VpnHood.Core.Common/_publish.ps1";
	& "$solutionDir/Core/VpnHood.Core.Tunneling/_publish.ps1";
	& "$solutionDir/Core/VpnHood.Core.Client/_publish.ps1";
	& "$solutionDir/Core/VpnHood.Core.Client.Device.Android/_publish.ps1";
	& "$solutionDir/Core/VpnHood.Core.Client.Device/_publish.ps1";
	& "$solutionDir/Core/VpnHood.Core.Client.Device.WinDivert/_publish.ps1";
	& "$solutionDir/Core/VpnHood.Core.Server/_publish.ps1";
	& "$solutionDir/Core/VpnHood.Core.Server.Access/_publish.ps1";
	& "$solutionDir/Core/VpnHood.Core.Server.Access.FileAccessManager/_publish.ps1";

	& "$solutionDir/AppLibs/VpnHood.AppLibs.Abstractions/_publish.ps1";
	& "$solutionDir/AppLibs/VpnHood.AppLibs/_publish.ps1";
	& "$solutionDir/AppLibs/VpnHood.AppLibs.Resources/_publish.ps1";
	& "$solutionDir/AppLibs/VpnHood.AppLibs.WebServer/_publish.ps1";
	& "$solutionDir/AppLibs/VpnHood.AppLibs.Store/_publish.ps1";
	& "$solutionDir/AppLibs/VpnHood.AppLibs.Android.Common/_publish.ps1";
	& "$solutionDir/AppLibs/VpnHood.AppLibs.Android.GooglePlay/_publish.ps1";
	& "$solutionDir/AppLibs/VpnHood.AppLibs.Android.GooglePlay.Core/_publish.ps1";
	& "$solutionDir/AppLibs/VpnHood.AppLibs.Android.Ads.AdMob/_publish.ps1";
	& "$solutionDir/AppLibs/VpnHood.AppLibs.Win.Common/_publish.ps1";
	& "$solutionDir/AppLibs/VpnHood.AppLibs.Win.Common.WpfSpa/_publish.ps1";
}

# publish MAUI nugets
if ($maui) {
	& "$solutionDir/AppLibs/VpnHood.AppLibs.Maui.Common/_publish.ps1";
}

# publish win client
if ($clientWin) {
	& "$solutionDir/VpnHood.Apps/VpnHood.AppLibs.Win.Client/_publish.ps1";
}

# publish win client
if ($connectWin) {
	& "$solutionDir/VpnHood.Apps/VpnHood.AppLibs.Win.Connect/_publish.ps1";
}

# publish server
if ($server) {	
	& "$solutionDir/VpnHood.Apps/VpnHood.Core.Server.App.Net/Pub/publish_win.ps1";
	& "$solutionDir/VpnHood.Apps/VpnHood.Core.Server.App.Net/Pub/publish_linux_x64.ps1";
	& "$solutionDir/VpnHood.Apps/VpnHood.Core.Server.App.Net/Pub/publish_linux_arm64.ps1";
	& "$solutionDir/VpnHood.Apps/VpnHood.Core.Server.App.Net/Pub/publish_docker.ps1" -distribute $distribute;
}

# publish android
if ($clientAndroid) {	
	& "$solutionDir/VpnHood.Apps/VpnHood.AppLibs.Android.Client.Google/_publish.ps1";
	& "$solutionDir/VpnHood.Apps/VpnHood.AppLibs.Android.Client.Web/_publish.ps1";
}

# publish android
if ($connectAndroid) {	
	& "$solutionDir/VpnHood.Apps/VpnHood.AppLibs.Android.Connect.Google/_publish.ps1";
	& "$solutionDir/VpnHood.Apps/VpnHood.AppLibs.Android.Connect.Web/_publish.ps1";
}


# distribute
if ($distribute) {
    & "$PSScriptRoot/PublishToGitHub.ps1" `
		-mainRepo ($clientWin -or $clientAndroid -or $server) `
		-connectRepo ($connectWin -or $connectAndroid);
}


# update and push samples nugets
if ($samples) {
	& "$solutionDir/../VpnHood.Core.Client.Samples/UpdateAndPush.ps1";
}

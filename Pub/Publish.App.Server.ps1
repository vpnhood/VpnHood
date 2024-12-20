param( 
	[Parameter(Mandatory=$true)][object]$bump,
	[Parameter(Mandatory=$true)][object]$nugets,
	[Parameter(Mandatory=$true)][object]$distribute,
	);

$nugets = $nugets -eq "1";
$distribute = $distribute -eq "1";

. "$PSScriptRoot/Core/Common.ps1" -bump $bump

# clean all
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

& "$solutionDir/Src/Apps/Server.Net/Pub/publish_win.ps1";
& "$solutionDir/Src/Apps/Server.Net/Pub/publish_linux_x64.ps1";
& "$solutionDir/Src/Apps/Server.Net/Pub/publish_linux_arm64.ps1";
& "$solutionDir/Src/Apps/Server.Net/Pub/publish_docker.ps1" -distribute $distribute;

# distribute
if ($distribute) {
    & "$PSScriptRoot/PublishToGitHub.Server.ps1";
}

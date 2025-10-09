param( 
	[Parameter(Mandatory=$true)][object]$bump,
	[Parameter(Mandatory=$true)][object]$nugets,
	[Parameter(Mandatory=$true)][object]$windows,
	[Parameter(Mandatory=$true)][object]$linux,
	[Parameter(Mandatory=$true)][object]$android,
	[Parameter(Mandatory=$true)][object]$distribute,
	[Parameter(Mandatory=$true)][object]$samples,
	[int]$rollout,
	[switch]$cleanall
);

. "$PSScriptRoot/../Core/Common.ps1" -bump $bump

$windows = $windows -eq "1";
$linux = $linux -eq "1";
$android = $android -eq "1";
$distribute = $distribute -eq "1";
$rollout = Get-RolloutPercentage -distribute $distribute -rollout $rollout

# clean all
if ($cleanall) {
	& $msbuild $solutionDir /p:Configuration=Release /t:Clean /verbosity:$msverbosity;
}

# clean old release notes
Remove-Item "$packagesRootDir/$packageClientDirName/ReleaseNote.txt" -ErrorAction Ignore;

# rebuild libraries
if ($nugets) {
	# generic
	& "$solutionDir/Src/Core/VpnHood.Core.Toolkit/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Packets/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.PacketTransports/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.VpnAdapters.Abstractions/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.VpnAdapters.AndroidTun/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.VpnAdapters.WinTun/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.VpnAdapters.LinuxTun/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.VpnAdapters.WinDivert/_publish.ps1";

	# core
	& "$solutionDir/Src/Core/VpnHood.Core.Common/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Tunneling/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Client.Abstractions/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Client/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Client.VpnServices.Abstractions/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Client.VpnServices.Host/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Client.VpnServices.Manager/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Client.Device/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Client.Device.Android/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Client.Device.Win/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Client.Device.Linux/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Server/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Server.Access/_publish.ps1";
	& "$solutionDir/Src/Core/VpnHood.Core.Server.Access.FileAccessManager/_publish.ps1";

	# applib
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.Abstractions/_publish.ps1";
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.App/_publish.ps1";
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.WebServer/_publish.ps1";
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.Store/_publish.ps1";
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.Android.Common/_publish.ps1";
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.Android.GooglePlay/_publish.ps1";
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.Android.GooglePlay.Core/_publish.ps1";
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.Android.Ads.AdMob/_publish.ps1";
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.Win.Common/_publish.ps1";
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.Win.Common.WpfSpa/_publish.ps1";
	& "$solutionDir/Src/AppLib/VpnHood.AppLib.Maui.Common/_publish.ps1";
}

if ($windows) {
	& "$solutionDir/Src/Apps/Client.Win.Web/_publish.ps1";
}

if ($linux) {
	& "$solutionDir/Src/Apps/Client.Linux.Web/_publish.ps1";
}

if ($android) {
	& "$solutionDir/Src/Apps/Client.Android.Google/_publish.ps1";
	& "$solutionDir/Src/Apps/Client.Android.Web/_publish.ps1";
}

# distribute
if ($distribute) {
    & "$PSScriptRoot/PublishToGitHub.ps1";
}

# update and push samples nugets
if ($samples) {
	& "$solutionDir/../VpnHood.App.Samples/UpdateAndPush.ps1";
}

# commit and push git
if (!$prerelease) {
	Write-Host "Pushing to main branch..." -ForegroundColor Magenta;
	git --git-dir=$gitDir --work-tree=$solutionDir push origin development:main --force;
}
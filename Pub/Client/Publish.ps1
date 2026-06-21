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
	& "$PSScriptRoot/PublishNugets.ps1";
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
	& "$solutionDir/Src/Apps/Client.Android.Web/_publish-arm64.ps1";
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
	git --git-dir=$gitDir --work-tree=$solutionDir commit -a -m "Release version $versionParam";
	git --git-dir=$gitDir --work-tree=$solutionDir push origin development:main;
}
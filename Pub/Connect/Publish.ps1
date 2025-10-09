param( 
	[Parameter(Mandatory=$true)][object]$bump,
	[Parameter(Mandatory=$true)][object]$windows,
	[Parameter(Mandatory=$true)][object]$linux,
	[Parameter(Mandatory=$true)][object]$android,
	[Parameter(Mandatory=$true)][object]$distribute,
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

# Remove old ReleaseNote
Remove-Item "$packagesRootDir/$packageConnectDirName/ReleaseNote.txt" -ErrorAction Ignore;

if ($windows) {
	& "$solutionDir/Src/Apps/Connect.Win.Web/_publish.ps1";
}

if ($linux) {
	& "$solutionDir/Src/Apps/Connect.Linux.Web/_publish.ps1";
}

if ($android) {
	& "$solutionDir/Src/Apps/Connect.Android.Google/_publish.ps1";
	& "$solutionDir/Src/Apps/Connect.Android.Web/_publish.ps1";
}

# distribute
if ($distribute) {
    & "$PSScriptRoot/PublishToGitHub.ps1";
}
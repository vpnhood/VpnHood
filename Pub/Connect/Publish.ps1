param( 
	[Parameter(Mandatory=$true)][object]$bump,
	[Parameter(Mandatory=$true)][object]$distribute,
	[int]$rollout
	);

. "$PSScriptRoot/../Core/Common.ps1" -bump $bump

$distribute = $distribute -eq "1";
$rollout = Get-RolloutPercentage -distribute $distribute -rollout $rollout

# Remove old ReleaseNote
Remove-Item "$packagesRootDir/$packageConnectDirName/ReleaseNote.txt" -ErrorAction Ignore;

& "$solutionDir/Src/Apps/Connect.Win.Web/_publish.ps1";
& "$solutionDir/Src/Apps/Connect.Linux.Web/_publish.ps1";
& "$solutionDir/Src/Apps/Connect.Android.Google/_publish.ps1";
& "$solutionDir/Src/Apps/Connect.Android.Web/_publish.ps1";

# distribute
if ($distribute) {
    & "$PSScriptRoot/PublishToGitHub.ps1";
}
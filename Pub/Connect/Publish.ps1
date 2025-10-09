param( 
	[Parameter(Mandatory=$true)][object]$bump,
	[Parameter(Mandatory=$true)][object]$distribute,
	[int]$rollout
	);

$distribute = $distribute -eq "1";

. "$PSScriptRoot/../Core/Common.ps1" -bump $bump

# prompt for rollout if $distribute is set and rollout is 0
if ($distribute -and ($rollout -le 0 -or $rollout -gt 100)) {
	[int]$parsedRollout = 0
	$rolloutInput = Read-Host "Enter rollout percentage (1-100, default 100)";
	if ([string]::IsNullOrWhiteSpace($rolloutInput)) {
		$rollout = 100;
	} elseif ([int]::TryParse($rolloutInput, [ref]$parsedRollout) -and $parsedRollout -ge 1 -and $parsedRollout -le 100) {
		$rollout = $parsedRollout;
	} else {
		throw "Invalid rollout.";
	}
}

# clean all
& $msbuild $solutionDir /p:Configuration=Release /t:Clean /verbosity:$msverbosity;
$noPushNuget = !$nugets

Remove-Item "$packagesRootDir/$packageConnectDirName/ReleaseNote.txt" -ErrorAction Ignore;

& "$solutionDir/Src/Apps/Connect.Win.Web/_publish.ps1";
& "$solutionDir/Src/Apps/Connect.Linux.Web/_publish.ps1";
& "$solutionDir/Src/Apps/Connect.Android.Google/_publish.ps1";
& "$solutionDir/Src/Apps/Connect.Android.Web/_publish.ps1";

# distribute
if ($distribute) {
    & "$PSScriptRoot/PublishToGitHub.ps1";
}
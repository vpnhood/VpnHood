param( 
	[Parameter(Mandatory=$true)][object]$bump,
	[Parameter(Mandatory=$true)][object]$docker,
	[Parameter(Mandatory=$true)][object]$distribute
	);

$distribute = $distribute -eq "1";
$docker = $docker -eq "1";

. "$PSScriptRoot/../Core/Common.ps1" -bump $bump

# remove old release note
Remove-Item "$packagesRootDir/$packageServerDirName/ReleaseNote.txt" -ErrorAction Ignore;

& "$solutionDir/Src/Apps/Server.Net/_publish.ps1";

if ($docker) {
	& "$solutionDir/Src/Apps/Server.Net/Pub/publish_docker.ps1" -distribute $distribute;
}

# distribute
if ($distribute) {
    & "$PSScriptRoot/PublishToGitHub.ps1";
}

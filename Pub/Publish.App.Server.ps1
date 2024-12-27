param( 
	[Parameter(Mandatory=$true)][object]$bump,
	[Parameter(Mandatory=$true)][object]$distribute
	);

$distribute = $distribute -eq "1";

. "$PSScriptRoot/Core/Common.ps1" -bump $bump

# clean all
Remove-Item "$packagesRootDir/ReleaseNote.Server.txt" -ErrorAction Ignore;

& "$solutionDir/Src/Apps/Server.Net/Pub/publish_win.ps1";
& "$solutionDir/Src/Apps/Server.Net/Pub/publish_linux_x64.ps1";
& "$solutionDir/Src/Apps/Server.Net/Pub/publish_linux_arm64.ps1";
& "$solutionDir/Src/Apps/Server.Net/Pub/publish_docker.ps1" -distribute $distribute;

# distribute
if ($distribute) {
    & "$PSScriptRoot/PublishToGitHub.Server.ps1";
}

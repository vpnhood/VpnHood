param(
	[Parameter(Mandatory=$true)][object]$docker,
	[Parameter(Mandatory=$true)][object]$distribute
	);

$distribute = $distribute -eq "1";
$docker = $docker -eq "1";

# This no longer bumps the version — run Pub/Bump.ps1 -server first (bumping is CI/Bump.ps1's job).
# NOTE: Server is not yet migrated to a CI publish workflow, so unlike Client/Connect it still
# distributes locally below. Remove the distribute path once server_publish.yml exists.
. "$PSScriptRoot/../Lib/Common.ps1"

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

param(
	# Also build a LOCAL, host-arch docker image (buildx --load) for smoke testing. Never pushes.
	[object]$docker = "0"
	);

$docker = $docker -eq "1";

# Build-only, for LOCAL smoke testing. Distribution (the GitHub release + the multi-arch docker push)
# happens ONLY in CI via .github/workflows/server_publish.yml in the vpnhood/VpnHood.App.Server repo
# (dispatched by pub/Server/PublishByGithub.ps1) — never from a developer machine. This mirrors
# pub/Client/Publish.ps1. Version bumping is bump.yml's job (pub/Invoke-VersionBump.ps1); this script never bumps.
. "$PSScriptRoot/../lib/Common.ps1"

# remove old release note
Remove-Item "$packagesRootDir/$packageServerDirName/ReleaseNote.txt" -ErrorAction Ignore;

# build the linux + windows-x64 server packages into pub/bin/<tag>/VpnHoodServer
& "$solutionDir/src/Apps/Server.Net/_publish.ps1";

# optional local docker smoke build (host arch only, --load, no push)
if ($docker) {
	& "$solutionDir/src/Apps/Server.Net/pub/publish_docker.ps1" -distribute 0;
}

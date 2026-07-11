$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));

# Resolve the release repo (where the generated install/update *.json URLs point). In CI the
# server_publish.yml workflow runs INSIDE the release repo, so VH_PUBLISH_REPO / GITHUB_REPOSITORY
# name it (a fork thus publishes to its own server repo). On a local desktop smoke build neither is
# set, so fall back to the canonical repo. ResolvePublishRepo.ps1 is side-effect free (no version bump),
# so it is safe to dot-source here without pulling in Common.ps1.
. "$SolutionDir/pub/Lib/ResolvePublishRepo.ps1";
$repoBaseUrl =
	if (-not [string]::IsNullOrWhiteSpace($env:VH_PUBLISH_REPO) -or -not [string]::IsNullOrWhiteSpace($env:GITHUB_REPOSITORY)) {
		Resolve-PublishRepoUrl;
	}
	else {
		"https://github.com/vpnhood/VpnHood.App.Server";
	}

& "$SolutionDir/pub/Lib/vh_installer/publish.ps1" `
	-projectDir $PSScriptRoot `
	-repoBaseUrl $repoBaseUrl `
	-publishDirName "VpnHoodServer" `
	-os "linux" `
	-launcherName "vhserver";

& "$SolutionDir/pub/Lib/vh_installer/publish_impl.ps1" `
	-projectDir $PSScriptRoot `
	-repoBaseUrl $repoBaseUrl `
	-publishDirName "VpnHoodServer" `
	-os "win" `
	-cpu "x64" `
	-launcherName "vhserver";

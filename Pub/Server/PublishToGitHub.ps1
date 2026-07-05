Write-Host "*** Publish VpnHood! SERVER to GitHub" -BackgroundColor Blue

. "$PSScriptRoot/../Lib/Common.ps1"
. "$PSScriptRoot/../Lib/utils/changelog_utils.ps1"

# set Variables
$env:GITHUB_TOKEN = Get-Content "$userDir/github_publish_apikey.txt";
$packageFileTitle = $packageServerDirName;
$packageDir = "$releaseRootDir/$packageFileTitle";
$packageLatestDir = "$releaseRootDir/$packageFileTitle";
$repoName = "vpnhood/VpnHood.App.Server";
$changeLogFileName = "CHANGELOG.Server.md";

# create release note. The CHANGELOG is maintained by hand (leading "# Latest" section); this only
# READS it — extract the first H1 section, dropping the other product's lines.
$changeLog = Get-Content "$solutionDir/$changeLogFileName" -Raw;
$releaseNote = Changelog_GetRecentSecion $changeLog @("#client");
$releaseNote | Out-File -FilePath "$packageDir/ReleaseNote.txt" -Encoding utf8 -Force -NoNewline;
if ($isLatest) {
	$releaseNote | Out-File -FilePath "$packageLatestDir/ReleaseNote.txt" -Encoding utf8 -Force -NoNewline;
}

# delete old release if exists
Write-Host "delete old release if exists: $versionTag";
$releaseExists = gh release view "$versionTag" --repo $repoName 2>&1;
if ($LASTEXITCODE -eq 0) {
	gh release delete "$versionTag" --repo $repoName --cleanup-tag --yes;
}

# publish new releasegh release delete "$versionTag" --cleanup-tag --yes;
gh release create "$versionTag" `
	--repo $repoName `
	--title "$versionTag" `
	$releaseFlag `
	-F $packageDir/ReleaseNote.txt `
	$packageDir/linux-any/VpnHoodServer-linux-msquic.sh `
	$packageDir/linux-any/VpnHoodServer-linux.sh `
	$packageDir/linux-x64/VpnHoodServer-linux-x64.json `
	$packageDir/linux-x64/VpnHoodServer-linux-x64.sh `
	$packageDir/linux-x64/VpnHoodServer-linux-x64.tar.gz `
	$packageDir/linux-arm64/VpnHoodServer-linux-arm64.json `
	$packageDir/linux-arm64/VpnHoodServer-linux-arm64.sh `
	$packageDir/linux-arm64/VpnHoodServer-linux-arm64.tar.gz `
	$packageDir/win-x64/VpnHoodServer-win-x64.json `
	$packageDir/win-x64/VpnHoodServer-win-x64.ps1 `
	$packageDir/win-x64/VpnHoodServer-win-x64.zip `
	$packageDir/docker/VpnHoodServer.docker.yml `
	$packageDir/docker/VpnHoodServer.docker.sh;
	
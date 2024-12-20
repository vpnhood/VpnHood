. "$PSScriptRoot/Core/Common.ps1"

$changeLogFileName = "CHANGELOG.Server.md";
$changeLogReleaseFileName = "ReleaseNote.Server.txt";

# update CHANGELOG
$text = Get-Content "$solutionDir/$changeLogFileName" -Raw;

# find top version
$vStart = $text.IndexOf("#");
$vEnd = $text.IndexOf("`n", $vStart) - 1;
$topVersion = $text.SubString($vStart, $vEnd - $vStart);

# change top version
$changeLog = $text -replace $topVersion, "# v$versionParam";
$changeLog  | Out-File -FilePath "$solutionDir/$changeLogFileName" -Encoding utf8 -Force -NoNewline;

# create release note
$releaseNote = $changeLog;
$releaseNote = $releaseNote.SubString($releaseNote.IndexOf("`n")); # remove version tag
$releaseNote = $releaseNote.SubString(0, $releaseNote.IndexOf("`n# ")); # remove other version
$releaseNote | Out-File -FilePath "$packagesRootDir/$changeLogReleaseFileName" -Encoding utf8 -Force -NoNewline;
if ($isLatest) {
	$releaseNote | Out-File -FilePath "$packagesRootDirLatest/$changeLogReleaseFileName" -Encoding utf8 -Force -NoNewline;
}

#-----------
# commit and push git
#-----------
PushMainRepo;

#-----------
# CONNECT REPO
#-----------
Write-Host "*** Publish VpnHood! SERVER releases" -BackgroundColor Blue;

# set Connect Variables
$serverRepoDir = Join-Path $vhDir "VpnHood.App.Server";
	
# Publishing to GitHub
Push-Location -Path $serverRepoDir;

gh release delete "$versionTag" --cleanup-tag --yes;
gh release create "$versionTag" `
	--title "$versionTag" `
	(&{if($prerelease) {"--prerelease"} else {"--latest"}}) `
	-F $releaseRootDir/$changeLogReleaseFileName `
	$releaseRootDir/$packageServerDirName/linux-x64/VpnHoodServer-linux-x64.json `
	$releaseRootDir/$packageServerDirName/linux-x64/VpnHoodServer-linux-x64.sh `
	$releaseRootDir/$packageServerDirName/linux-x64/VpnHoodServer-linux-x64.tar.gz `
	$releaseRootDir/$packageServerDirName/linux-arm64/VpnHoodServer-linux-arm64.json `
	$releaseRootDir/$packageServerDirName/linux-arm64/VpnHoodServer-linux-arm64.sh `
	$releaseRootDir/$packageServerDirName/linux-arm64/VpnHoodServer-linux-arm64.tar.gz `
	$releaseRootDir/$packageServerDirName/win-x64/VpnHoodServer-win-x64.json `
	$releaseRootDir/$packageServerDirName/win-x64/VpnHoodServer-win-x64.ps1 `
	$releaseRootDir/$packageServerDirName/win-x64/VpnHoodServer-win-x64.zip `
	$releaseRootDir/$packageServerDirName/docker/VpnHoodServer.docker.yml `
	$releaseRootDir/$packageServerDirName/docker/VpnHoodServer.docker.sh;
	
Pop-Location

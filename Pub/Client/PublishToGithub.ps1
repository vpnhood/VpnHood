Write-Host "*** Publish VpnHood! CONNECT releases" -BackgroundColor Blue

. "$PSScriptRoot/../Core/Common.ps1"
. "$PSScriptRoot/../Core/utils/changelog_utils.ps1"

# set Variables
$env:GITHUB_TOKEN = Get-Content "$userDir/github_publish_apikey.txt";
$packageDir = "$releaseRootDir/$packageConnectDirName";
$packageLatestDir = "$releaseRootDir/$packageConnectDirName";
$repoName = "vpnhood/VpnHood.App.Connect";
$workflowId = "publish-googleplay.yml";

# update CHANGELOG
$changeLog = Get-Content "$solutionDir/CHANGELOG.md" -Raw;

# find top version
$changeLog = (Changelog_UpdateHeader $changeLog "v$versionParam");
$changeLog | Out-File -FilePath "$solutionDir/CHANGELOG.md" -Encoding utf8 -Force -NoNewline;

# create release note
$releaseNote = Changelog_GetRecentSecion $changeLog @("#client");
$releaseNote | Out-File -FilePath "$packageDir/ReleaseNote.txt" -Encoding utf8 -Force -NoNewline;
if ($isLatest) {
	$releaseNote | Out-File -FilePath "$packageLatestDir/ReleaseNote.txt" -Encoding utf8 -Force -NoNewline;
}

# Update fastlane changelog in repo 
$fastlaneChangelogPath = "fastlane/metadata/android/en-US/changelogs/$versionCode.txt";
Write-Host "Update default.txt of connect repo from changeLog ..."
$storeChangelog = Changelog_GetRecentSecion $changeLog @("windows:", "linux:", "developer:");
$storeChangelog = $storeChangelog -replace "Android: ", "";
PushTextToRepo $repoName $fastlaneChangelogPath $storeChangelog

# delete old release if exists
Write-Host "delete old release if exists: $versionTag";
$releaseExists = gh release view "$versionTag" --repo $repoName 2>&1;
if ($LASTEXITCODE -eq 0) {
	gh release delete "$versionTag" --repo $repoName --cleanup-tag --yes;
}

# publish new release
Write-Host "create new release: $versionTag";
gh release create "$versionTag" `
	--repo $repoName `
	--title "$versionTag" `
	-F $packageDir/ReleaseNote.txt `
	$releaseFlag `
	$packageDir/android-google/VpnHoodConnect-android.aab `
	$packageDir/android-google/VpnHoodConnect-android.aab.json `
	$packageDir/android-web/VpnHoodConnect-android-web.apk `
	$packageDir/android-web/VpnHoodConnect-android-web.json `
	$packageDir/linux-x64/VpnHoodConnect-linux-x64.tar.gz `
	$packageDir/linux-x64/VpnHoodConnect-linux-x64.json `
	$packageDir/linux-x64/VpnHoodConnect-linux-x64.sh `
	$packageDir/linux-arm64/VpnHoodConnect-linux-arm64.tar.gz `
	$packageDir/linux-arm64/VpnHoodConnect-linux-arm64.json `
	$packageDir/linux-arm64/VpnHoodConnect-linux-arm64.sh `
	$packageDir/linux-any/VpnHoodConnect-linux.sh `
	$packageDir/windows-web/VpnHoodConnect-win-x64.msi `
	$packageDir/windows-web/VpnHoodConnect-win-x64.json `
	$packageDir/windows-web/VpnHoodConnect-win-x64.txt;

if ($LASTEXITCODE -ne 0) {
    $code = $LASTEXITCODE
    throw "Failed to create GitHub release. Exit code: $code"
}

# Run workflow on GitHub. The id is $workflowId
# Write-Host "Triggering GitHub workflow: $workflowId";
# gh workflow run "$workflowId" --repo $repoName;
# if ($LASTEXITCODE -ne 0) {
#    $code = $LASTEXITCODE
#    throw "Failed to create GitHub release. Exit code: $code"
#}


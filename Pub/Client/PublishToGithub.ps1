Write-Host "*** Publish VpnHood! releases" -BackgroundColor Blue

. "$PSScriptRoot/../Core/Common.ps1"
. "$PSScriptRoot/../Core/utils/changelog_utils.ps1"

# set Variables
$env:GITHUB_TOKEN = Get-Content "$userDir/github_publish_apikey.txt";
$packageFileTitle = $packageClientDirName;
$packageDir = "$releaseRootDir/$packageClientDirName";
$packageLatestDir = "$releaseRootDir/$packageClientDirName";
$repoName = "vpnhood/VpnHood";

# update CHANGELOG
$changeLog = Get-Content "$solutionDir/CHANGELOG.md" -Raw;

# find top version
$changeLog = (Changelog_UpdateHeader $changeLog "v$versionParam");
$changeLog | Out-File -FilePath "$solutionDir/CHANGELOG.md" -Encoding utf8 -Force -NoNewline;

# create release note
$releaseNote = Changelog_GetRecentSecion $changeLog @("#connect");
$releaseNote | Out-File -FilePath "$packageDir/ReleaseNote.txt" -Encoding utf8 -Force -NoNewline;
if ($isLatest) {
	$releaseNote | Out-File -FilePath "$packageLatestDir/ReleaseNote.txt" -Encoding utf8 -Force -NoNewline;
}

# Update fastlane changelog in repo 
$fastlaneChangelogPath = "fastlane/metadata/android/en-US/changelogs/$versionCode.txt";
Write-Host "Update default.txt of repo from changeLog ..."
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
	$packageDir/android-google/$packageFileTitle-android.aab `
	$packageDir/android-google/$packageFileTitle-android.aab.json `
	$packageDir/android-web/$packageFileTitle-android-web.apk `
	$packageDir/android-web/$packageFileTitle-android-web.json `
	$packageDir/linux-x64/$packageFileTitle-linux-x64.tar.gz `
	$packageDir/linux-x64/$packageFileTitle-linux-x64.json `
	$packageDir/linux-x64/$packageFileTitle-linux-x64.sh `
	$packageDir/linux-arm64/$packageFileTitle-linux-arm64.tar.gz `
	$packageDir/linux-arm64/$packageFileTitle-linux-arm64.json `
	$packageDir/linux-arm64/$packageFileTitle-linux-arm64.sh `
	$packageDir/linux-any/$packageFileTitle-linux.sh `
	$packageDir/windows-web/$packageFileTitle-win-x64.msi `
	$packageDir/windows-web/$packageFileTitle-win-x64.json `
	$packageDir/windows-web/$packageFileTitle-win-x64.txt;

if ($LASTEXITCODE -ne 0) {
    $code = $LASTEXITCODE
    throw "Failed to create GitHub release. Exit code: $code"
}

# commit and push to main
if (-not $prerelease){
	CommitAndPushToMainRepo
}

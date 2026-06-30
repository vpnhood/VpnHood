Write-Host "*** Publish VpnHood! releases" -BackgroundColor Blue

. "$PSScriptRoot/../Core/Common.ps1"
. "$PSScriptRoot/../Core/utils/changelog_utils.ps1"

# set Variables
# Only set the publish token from .user if one isn't already provided (e.g. an
# ambient GITHUB_TOKEN / gh auth when testing against a fork).
$tokenFile = "$userDir/github_publish_apikey.txt";
if (-not $env:GITHUB_TOKEN -and (Test-Path $tokenFile)) {
	$env:GITHUB_TOKEN = Get-Content $tokenFile;
}
$packageFileTitle = $packageClientDirName;
# Honor an optional artifact-title override (publish.json PackageTitle) so the asset file names here
# match what the build produced. The package DIR stays keyed by the stable folder name.
$titleOverride = (Get-AppPublishConfig $packageClientDirName).packageFileTitle;
if ($titleOverride) { $packageFileTitle = $titleOverride; }
$packageDir = "$releaseRootDir/$packageClientDirName";
$packageLatestDir = "$releaseRootDir/$packageClientDirName";
# Target repo: defaults to the CURRENT repo (so a fork publishes to itself) and is overridable
# with VH_PUBLISH_REPO. Resolved in Common.ps1 (see ResolvePublishRepo.ps1).
$repoName = $publishRepo;

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
# --- TEMP: disabled for the Linux-only publish (this is Android/Play store metadata).
#          Restore this block for a full multi-platform release. ---
# $fastlaneChangelogPath = "fastlane/metadata/android/en-US/changelogs/$versionCode.txt";
# Write-Host "Update default.txt of repo from changeLog ..."
# $storeChangelog = Changelog_GetRecentSecion $changeLog @("windows:", "linux:", "developer:");
# $storeChangelog = $storeChangelog -replace "Android: ", "";
# PushTextToRepo $repoName $fastlaneChangelogPath $storeChangelog

# delete old release if exists
Write-Host "delete old release if exists: $versionTag";
$releaseExists = gh release view "$versionTag" --repo $repoName 2>&1;
if ($LASTEXITCODE -eq 0) {
	gh release delete "$versionTag" --repo $repoName --cleanup-tag --yes;
}

# publish new release
Write-Host "create new release: $versionTag";

# Assets to attach (Linux + Windows + Android).
$assets = @(
	"$packageDir/linux-x64/$packageFileTitle-linux-x64.tar.gz",
	"$packageDir/linux-x64/$packageFileTitle-linux-x64.json",
	"$packageDir/linux-x64/$packageFileTitle-linux-x64.sh",
	"$packageDir/linux-arm64/$packageFileTitle-linux-arm64.tar.gz",
	"$packageDir/linux-arm64/$packageFileTitle-linux-arm64.json",
	"$packageDir/linux-arm64/$packageFileTitle-linux-arm64.sh",
	"$packageDir/linux-any/$packageFileTitle-linux.sh",

	"$packageDir/windows-web/$packageFileTitle-win-x64.msi",
	"$packageDir/windows-web/$packageFileTitle-win-x64.json",
	"$packageDir/windows-web/$packageFileTitle-win-x64.txt",

	"$packageDir/android-google/$packageFileTitle-android.aab",
	"$packageDir/android-google/$packageFileTitle-android.aab.json",
	"$packageDir/android-web/$packageFileTitle-android-web.apk",
	"$packageDir/android-web/$packageFileTitle-android-web.json",
	"$packageDir/android-arm64-web/$packageFileTitle-android-arm64-web.apk",
	"$packageDir/android-arm64-web/$packageFileTitle-android-arm64-web.json"
);

gh release create "$versionTag" `
	--repo $repoName `
	--title "$versionTag" `
	-F $packageDir/ReleaseNote.txt `
	$releaseFlag `
	$assets;

if ($LASTEXITCODE -ne 0) {
    $code = $LASTEXITCODE
    throw "Failed to create GitHub release. Exit code: $code"
}

# commit and push to main
if (-not $prerelease){
	CommitAndPushToMainRepo
}

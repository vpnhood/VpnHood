param(
	# The three things that differ between a client and a connect release are passed IN, so this
	# script stays generic (no client/connect knowledge baked in). The per-product entry points that
	# supply them are Pub/Client/PublishToGithub.ps1 and Pub/Connect/PublishToGithub.ps1.
	# Bin/module folder name + .user app-config key, e.g. "VpnHoodClient" / "VpnHoodConnect".
	[Parameter(Mandatory = $true)] [string]$packageDirName,
	# GitHub repo to release to (owner/name), already resolved by the caller (Resolve-PublishRepoSlug).
	[Parameter(Mandatory = $true)] [string]$repoName,
	# The OTHER product's CHANGELOG tag, whose lines are dropped from this release note ("#connect" for
	# a client release, "#client" for a connect release).
	[Parameter(Mandatory = $true)] [string]$dropChangelogTag
)

Write-Host "*** Publish $packageDirName release to GitHub" -BackgroundColor Blue

. "$PSScriptRoot/Common.ps1"
. "$PSScriptRoot/utils/changelog_utils.ps1"

# Only set the publish token from .user if one isn't already provided (e.g. an ambient GITHUB_TOKEN
# / gh auth when running in CI). CI passes github.token via the environment.
$tokenFile = "$userDir/github_publish_apikey.txt";
if (-not $env:GITHUB_TOKEN -and (Test-Path $tokenFile)) {
	$env:GITHUB_TOKEN = Get-Content $tokenFile;
}

$packageFileTitle = $packageDirName;
# Honor an optional artifact-title override (publish.json PackageTitle) so the asset file names here
# match what the build produced. The package DIR stays keyed by the stable folder name.
$titleOverride = (Get-AppPublishConfig $packageDirName).packageFileTitle;
if ($titleOverride) { $packageFileTitle = $titleOverride; }
$packageDir = "$releaseRootDir/$packageDirName";
$packageLatestDir = "$releaseRootDir/$packageDirName";

# Read the CHANGELOG for the release note. The version header is already stamped by the bump (bump.yml
# via Pub/Bump.ps1); this workflow only reads the changelog — it never rewrites or commits it.
$changeLog = Get-Content "$solutionDir/CHANGELOG.md" -Raw;

# create release note (drop the other product's lines)
$releaseNote = Changelog_GetRecentSecion $changeLog @($dropChangelogTag);
$releaseNote | Out-File -FilePath "$packageDir/ReleaseNote.txt" -Encoding utf8 -Force -NoNewline;
if ($isLatest) {
	$releaseNote | Out-File -FilePath "$packageLatestDir/ReleaseNote.txt" -Encoding utf8 -Force -NoNewline;
}

# --- TEMP: disabled while the pipeline is being wired. These write to OTHER repos, which the ambient
#          github.token cannot do; re-enable with a dedicated PAT (see .github/DEPLOYMENT.md).
#   * push the per-version store changelog to the release repo's fastlane metadata
#   * (connect only) trigger the dl.github.io mirror update
# $fastlaneChangelogPath = "fastlane/metadata/android/en-US/changelogs/$versionCode.txt";
# $storeChangelog = Changelog_GetRecentSecion $changeLog @("windows:", "linux:", "developer:");
# $storeChangelog = $storeChangelog -replace "Android: ", "";
# PushTextToRepo $repoName $fastlaneChangelogPath $storeChangelog

# delete old release if exists
Write-Host "delete old release if exists: $versionTag";
$null = gh release view "$versionTag" --repo $repoName 2>&1;
if ($LASTEXITCODE -eq 0) {
	gh release delete "$versionTag" --repo $repoName --cleanup-tag --yes;
}

# publish new release
Write-Host "create new release: $versionTag";

# Assets to attach (Android + Linux + Windows). A platform can be intentionally skipped in CI (e.g.
# the Windows MSI when no Advanced Installer license, or Android when build_android is off), so warn
# about each missing asset rather than failing the whole release on a non-existent file path.
$assets = @(
	"$packageDir/android-google/$packageFileTitle-android.aab",
	# Update-info file for the Google build; named "-android.json" (NOT "-android.aab.json") to match
	# the URL the shipped app polls (Android.Google/AppConfigs.cs UpdateInfoUrl).
	"$packageDir/android-google/$packageFileTitle-android.json",
	# Google Play-signed universal APK (added by the publish-play-android CI job). Optional.
	"$packageDir/android-google/$packageFileTitle-android.apk",
	"$packageDir/android-web/$packageFileTitle-android-web.apk",
	"$packageDir/android-web/$packageFileTitle-android-web.json",
	"$packageDir/android-web-arm64/$packageFileTitle-android-web-arm64.apk",
	"$packageDir/android-web-arm64/$packageFileTitle-android-web-arm64.json",

	"$packageDir/linux-x64/$packageFileTitle-linux-x64.tar.gz",
	"$packageDir/linux-x64/$packageFileTitle-linux-x64.json",
	"$packageDir/linux-x64/$packageFileTitle-linux-x64.sh",
	"$packageDir/linux-arm64/$packageFileTitle-linux-arm64.tar.gz",
	"$packageDir/linux-arm64/$packageFileTitle-linux-arm64.json",
	"$packageDir/linux-arm64/$packageFileTitle-linux-arm64.sh",
	"$packageDir/linux-any/$packageFileTitle-linux.sh",

	"$packageDir/windows-web/$packageFileTitle-win-x64.msi",
	"$packageDir/windows-web/$packageFileTitle-win-x64.json",
	"$packageDir/windows-web/$packageFileTitle-win-x64.txt"
);

$missingAssets = $assets | Where-Object { -not (Test-Path $_) };
foreach ($missing in $missingAssets) {
	Write-Warning "Release asset not found, skipping: $missing";
}
$assets = $assets | Where-Object { Test-Path $_ };
if ($assets.Count -eq 0) {
	throw "No release assets were produced; aborting release creation.";
}

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

# NOTE: this workflow no longer commits/pushes or touches the changelog. The version bump is committed
# once, up front, by the bump (bump.yml via Pub/Bump.ps1); the changelog is hand-maintained and read
# only (first "# Latest" section). See Pub/RELEASE-STRATEGY.md.

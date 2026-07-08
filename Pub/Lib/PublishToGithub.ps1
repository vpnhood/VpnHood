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
	[Parameter(Mandatory = $true)] [string]$dropChangelogTag,
	# Which changelog file to read the release note from. Client/Connect share CHANGELOG.md; the Server
	# has its own CHANGELOG.Server.md.
	[Parameter(Mandatory = $false)] [string]$changelogFileName = "CHANGELOG.md",
	# Which asset layout to attach: "app" = the Client/Connect set (Android/Linux/Windows-MSI),
	# "server" = the Server set (Linux tar.gz + Windows-x64 zip + docker compose files). Keeping one
	# release creator for every product (see Pub/RELEASE-STRATEGY.md) instead of a per-product script.
	[Parameter(Mandatory = $false)] [ValidateSet("app", "server")] [string]$assetSet = "app"
)

Write-Host "*** Publish $packageDirName release to GitHub" -BackgroundColor Blue

. "$PSScriptRoot/Common.ps1"
. "$PSScriptRoot/utils/changelog_utils.ps1"

# gh reads its token from the environment: CI passes github.token as GITHUB_TOKEN; locally it uses
# your `gh auth login` (keyring) or an ambient GITHUB_TOKEN. No token file.

$packageFileTitle = $packageDirName;
# Honor an optional artifact-title override (publish.json PackageTitle) so the asset file names here
# match what the build produced. The package DIR stays keyed by the stable folder name.
$titleOverride = (Get-AppPublishConfig $packageDirName).packageFileTitle;
if ($titleOverride) { $packageFileTitle = $titleOverride; }
$packageDir = "$releaseRootDir/$packageDirName";
$packageLatestDir = "$releaseRootDir/$packageDirName";

# Read the CHANGELOG for the release note. The version header is already stamped by the bump (bump.yml
# via Pub/Bump.ps1); this workflow only reads the changelog — it never rewrites or commits it.
$changeLog = Get-Content "$solutionDir/$changelogFileName" -Raw;

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

# Assets to attach. A platform can be intentionally skipped in CI (e.g. the Windows MSI when no
# Advanced Installer license, Android when build_android is off, or Docker when the registry secrets
# are absent), so warn about each missing asset rather than failing the whole release on a
# non-existent file path.
if ($assetSet -eq "server") {
	# Server set: Linux (x64/arm64/any + msquic) tar.gz/sh/json, Windows-x64 zip/ps1/json, and the two
	# docker-compose helper files. The docker files keep their literal "VpnHoodServer.docker.*" names
	# (see Src/Apps/Server.Net/Pub/publish_docker.ps1), independent of $packageFileTitle.
	$assets = @(
		"$packageDir/linux-any/$packageFileTitle-linux-msquic.sh",
		"$packageDir/linux-any/$packageFileTitle-linux.sh",

		"$packageDir/linux-x64/$packageFileTitle-linux-x64.json",
		"$packageDir/linux-x64/$packageFileTitle-linux-x64.sh",
		"$packageDir/linux-x64/$packageFileTitle-linux-x64.tar.gz",
		"$packageDir/linux-arm64/$packageFileTitle-linux-arm64.json",
		"$packageDir/linux-arm64/$packageFileTitle-linux-arm64.sh",
		"$packageDir/linux-arm64/$packageFileTitle-linux-arm64.tar.gz",

		"$packageDir/win-x64/$packageFileTitle-win-x64.json",
		"$packageDir/win-x64/$packageFileTitle-win-x64.ps1",
		"$packageDir/win-x64/$packageFileTitle-win-x64.zip",

		"$packageDir/docker/VpnHoodServer.docker.yml",
		"$packageDir/docker/VpnHoodServer.docker.sh"
	);
}
else {
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
}

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

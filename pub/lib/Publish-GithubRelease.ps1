param(
	# The three things that differ between a client and a connect release are passed IN, so this
	# script stays generic (no client/connect knowledge baked in). The caller that supplies them is
	# .github/workflows/publish_app.yml (per-product values resolved there).
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
	# release creator for every product (see pub/RELEASE-STRATEGY.md) instead of a per-product script.
	[Parameter(Mandatory = $false)] [ValidateSet("app", "server")] [string]$assetSet = "app",
	# Exact commit the version tag must point at. It has to be a commit that EXISTS in $repoName, so only
	# the caller can resolve it (.github/workflows/publish_app.yml does). Empty = let GitHub choose, which
	# means its default-branch tip — acceptable only for a manual desktop run.
	[Parameter(Mandatory = $false)] [string]$targetCommitish = "",
	# Commit of the vpnhood/VpnHood code these binaries were built from, recorded in the release note.
	# A thin caller repo (Connect) carries no source, so its tag CANNOT point at the built code; this
	# line is then the only link between the release and the code that produced it.
	[Parameter(Mandatory = $false)] [string]$codeCommit = ""
)

Write-Host "*** Publish $packageDirName release to GitHub" -BackgroundColor Blue

. "$PSScriptRoot/Common.ps1"
. "$PSScriptRoot/utils/ChangelogUtils.ps1"

# gh reads its token from the environment: CI passes github.token as GITHUB_TOKEN; locally it uses
# your `gh auth login` (keyring) or an ambient GITHUB_TOKEN. No token file.

$packageFileTitle = $packageDirName;
# Honor an optional artifact-title override (publish.json PackageTitle) so the asset file names here
# match what the build produced. The package DIR stays keyed by the stable folder name.
$titleOverride = (Get-AppPublishConfig $packageDirName).packageFileTitle;
if ($titleOverride) { $packageFileTitle = $titleOverride; }
# $releaseRootDir already resolves to pub/bin/latest on a stable release and pub/bin/<tag> on a
# prerelease (pub/lib/Common.ps1), so this one path covers both — there is no separate "latest" dir to
# mirror into here, unlike the per-platform publishers that write pub/bin/<tag> and pub/bin/latest.
$packageDir = "$releaseRootDir/$packageDirName";

# Read the CHANGELOG for the release note. The version header is already stamped by the bump (bump.yml
# via pub/Invoke-VersionBump.ps1); this workflow only reads the changelog — it never rewrites or commits it.
$changeLog = Get-Content "$solutionDir/$changelogFileName" -Raw;

# create release note (drop the other product's lines)
$releaseNote = Changelog_GetRecentSecion $changeLog @($dropChangelogTag);

# Stamp the provenance of the binaries. The tag can only ever pin a commit in $repoName, and a thin
# caller repo (Connect) has no source of its own, so without this footer nothing on the release
# identifies which monorepo commit was actually built.
if ($codeCommit) {
	$releaseNote = $releaseNote.TrimEnd() + "`n`n---`nBuilt from vpnhood/VpnHood@$codeCommit`n";
}

$releaseNote | Out-File -FilePath "$packageDir/ReleaseNote.txt" -Encoding utf8 -Force -NoNewline;

# Replace a previous release for this version, but NEVER its tag — note the absence of --cleanup-tag.
# A version tag is immutable: once cut it keeps pointing at the commit it was cut from, forever.
# --cleanup-tag deleted the tag along with the release, and the `gh release create` below then recreated
# it at $repoName's DEFAULT BRANCH tip — so every re-publish silently MOVED the tag onto whatever was
# on that branch at that moment. Two things broke: the tag stopped identifying the released code (after
# Connect's default branch became `develop`, its tags landed on commits not even reachable from `main`),
# and it rewrote already-published history, leaving every clone that had fetched the old tag unable to
# push tags at all ("! [rejected] ... already exists"). Keeping the tag makes a re-publish idempotent:
# gh reuses the existing tag and only the release object is replaced.
Write-Host "delete old release if exists (its tag is kept): $versionTag";
$null = gh release view "$versionTag" --repo $repoName 2>&1;
if ($LASTEXITCODE -eq 0) {
	gh release delete "$versionTag" --repo $repoName --yes;
	if ($LASTEXITCODE -ne 0) {
		throw "Failed to delete the existing $versionTag release. Exit code: $LASTEXITCODE";
	}
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
	# (see src/Apps/Server.Net/pub/publish_docker.ps1), independent of $packageFileTitle.
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

# --target pins a NEW tag to an exact commit; without it GitHub creates the tag at $repoName's
# default-branch tip, i.e. wherever that branch happened to be rather than what was released. GitHub
# ignores it when the tag already exists, which is precisely what keeps an existing tag immutable
# across re-publishes (see the delete note above).
$targetArgs = if ($targetCommitish) { @("--target", $targetCommitish) } else { @() };

gh release create "$versionTag" `
	--repo $repoName `
	--title "$versionTag" `
	-F $packageDir/ReleaseNote.txt `
	@targetArgs `
	$releaseFlag `
	$assets;

if ($LASTEXITCODE -ne 0) {
	$code = $LASTEXITCODE
	throw "Failed to create GitHub release. Exit code: $code"
}

# NOTE: this workflow no longer commits/pushes or touches the changelog. The version bump is committed
# once, up front, by the bump (bump.yml via pub/Invoke-VersionBump.ps1); the changelog is hand-maintained and read
# only (first "# Latest" section). See pub/RELEASE-STRATEGY.md.

param(
	[Parameter(Mandatory=$true)] [String]$projectDir,
	# The .user/<appFolder>/ config folder name. Also the bin module dir name and the default artifact
	# title. packageFileTitle and repo-url are read from that folder's publish.json; each falls back to a
	# committed default when publish.json is absent. Mirrors PublishAndroidApp.ps1.
	[Parameter(Mandatory=$true)] [String]$appFolder,
	[Parameter(Mandatory=$true)] [String]$distribution,   # "ios"
	# Release repo for Connect (VH_CONNECT_PUBLISH_REPO) vs client; the URL itself is resolved below.
	[switch]$connect)

# iOS App Store publish, structured exactly like PublishAndroidApp.ps1 so the CI wiring and the
# release job treat it the same way: build -> $packagesRootDir/$appFolder/ios/<title>-ios.{ipa,json}.
#
# SIGNING is read from a marker the CI signing step writes (PrepareCiIosSigning.ps1) at
#   .user/<appFolder>/ios/ios_signing.json  ->  { "Signed": bool, "CodesignKey": "...", "AppProvision": "...", "ExtProvision": "..." }
# Unlike Android there is NO ephemeral fallback: an App Store .ipa cannot be self-signed. When the
# marker says Signed=false (distribution cert / profiles absent) we do a codesign-DISABLED build as a
# compile check and emit the sidecar json only (no .ipa) with a warning — the pipeline stays green and
# the App Store upload job simply finds no .ipa to send. Add the secrets to turn signing on.
#
# Requires the net11 iOS toolchain on PATH (this repo targets net11.0-ios): the .NET 11 SDK + `ios`
# workload and Xcode 26.5+. The csproj already sets ValidateXcodeVersion=false so Xcode 26.6 is accepted.

. "$PSScriptRoot/Common.ps1"

$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;

# Per-app identity from .user/<appFolder>/publish.json (see AppPublishConfig.ps1). The iOS BUNDLE ID is
# NOT part of publish.json's schema (it is Android-only), so it always comes from the csproj
# <BundleIdentifier> — never a throw when the (optional) iOS block is absent from a strict publish.json.
$appUserDir = Join-Path "$solutionDir/../.user/" $appFolder;
$appConfig = Get-AppPublishConfig $appFolder;
$packageId = ([Xml](Get-Content $projectFile)).Project.PropertyGroup.BundleIdentifier | Where-Object { $_ } | Select-Object -First 1;
if ([string]::IsNullOrWhiteSpace($packageId)) { Throw "No <BundleIdentifier> found in $projectFile."; }
$packageFileTitle = if ($appConfig.packageFileTitle) { $appConfig.packageFileTitle } else { $appFolder }
$repoUrl = if ($appConfig.repoUrl) { $appConfig.repoUrl } else { Resolve-PublishRepoUrl -Connect:$connect };
# iOS installs come from the App Store, so the "installation page" is the store/download page, not the
# .ipa (which iOS can't sideload). Fall back to the repo release when publish.json has no page.
$installationPageUrl = if ($appConfig.installationPageUrl) { $appConfig.installationPageUrl } else { $repoUrl };
# Strict: in strict mode (publish.json present) the app's shared appsettings must exist.
Assert-AppSettings $appFolder;

Write-Host "";
Write-Host "*** Publishing $projectFile (iOS) ..." -BackgroundColor Blue -ForegroundColor White;

# update project version (shared with the other platforms; stamps <ApplicationDisplayVersion>/etc.)
UpdateProjectVersion $projectFile;

# prepare module folder (keyed by the stable app folder, not the overridable artifact title)
$moduleDir = "$packagesRootDir/$appFolder/ios";
$moduleDirLatest = "$packagesRootDirLatest/$appFolder/ios";
PrepareModuleFolder $moduleDir $moduleDirLatest;

$module_baseFileName = "$packageFileTitle-ios";
$module_infoFile = "$moduleDir/$module_baseFileName.json";
$module_packageFile = "$moduleDir/$module_baseFileName.ipa";
$module_infoFileName = $(Split-Path "$module_infoFile" -leaf);
$module_packageFileName = $(Split-Path "$module_packageFile" -leaf);

# ----- signing marker (written by PrepareCiIosSigning.ps1 in CI, or by hand locally) -----
$iosDir = Join-Path $appUserDir "ios";
$signingFile = Join-Path $iosDir "ios_signing.json";
$signed = $false; $codesignKey = ""; $appProvision = ""; $extProvision = "";
if (Test-Path $signingFile) {
	$s = Get-Content $signingFile -Raw | ConvertFrom-Json;
	$signed = [bool]$s.Signed;
	$codesignKey = "$($s.CodesignKey)";
	$appProvision = "$($s.AppProvision)";
	$extProvision = "$($s.ExtProvision)";
}

$tfm = "net11.0-ios";
$rid = "ios-arm64";
$outputPath = Join-Path $projectDir "bin/Release-ios/publish/";

# iOS build number: App Store Connect requires a monotonically increasing CFBundleVersion. Reuse the
# patch component of the SemVer (matches the Android VersionCode fallback in PublishAndroidApp.ps1).
$buildNumber = if ($versionParam -match '^\d+\.\d+\.(\d+)$') { $Matches[1] } else { ($versionParam -replace '\D','') };

Write-Host;
Write-Host "*** Creating $module_packageFileName ..." -BackgroundColor Blue -ForegroundColor White;

try {
	if ($signed) {
		Write-Host "iOS distribution signing: identity='$codesignKey' appProfile='$appProvision' extProfile='$extProvision'" -ForegroundColor Cyan;

		# ArchiveOnBuild=true makes .NET-for-iOS produce an .xcarchive + .ipa. AutomaticProvisioning is
		# turned OFF so the build uses the EXACT App Store profiles installed by PrepareCiIosSigning.ps1
		# (a hosted CI runner isn't signed into an Apple account, so Xcode auto-signing can't run).
		# NOTE (verify on first real CI run): the Network Extension (.appex) needs its OWN App Store
		# profile. It is a ProjectReference, so if it doesn't pick up $extProvision here, set
		# CodesignProvision in Extension's csproj (or add a per-project override) — see DEPLOYMENT.md.
		dotnet publish $projectFile -c Release -f $tfm -r $rid `
			/verbosity:$msverbosity `
			/p:SolutionDir=$solutionDir `
			/p:RuntimeIdentifier=$rid `
			/p:ArchiveOnBuild=true `
			/p:Version=$versionParam `
			/p:ApplicationDisplayVersion=$versionParam `
			/p:ApplicationVersion=$buildNumber `
			/p:AutomaticProvisioning=false `
			/p:CodesignKey="$codesignKey" `
			/p:CodesignProvision="$appProvision" `
			/p:OutputPath=$outputPath `
			;
		if ($LASTEXITCODE -gt 0) { Throw "The iOS build exited with error code: $LASTEXITCODE"; }

		# Locate the produced .ipa (name follows the app; search the output + standard publish paths).
		$ipa = Get-ChildItem -Path $projectDir -Recurse -File -Filter "*.ipa" -ErrorAction SilentlyContinue |
			Sort-Object LastWriteTime -Descending | Select-Object -First 1;
		if (-not $ipa) { Throw "Signed build succeeded but no .ipa was found under $projectDir."; }
		Copy-Item -Path $ipa.FullName -Destination $module_packageFile -Force;
	}
	else {
		# No distribution signing -> emit the sidecar only, no .ipa. We deliberately DON'T run a device
		# build here: an ios-arm64 build requires code signing, so attempting one without a cert would
		# FAIL the job. Skipping it keeps the pipeline green (the whole point of skip-if-absent); the App
		# Store upload job then finds no .ipa and skips too. Add the signing secrets to switch it on.
		Write-Host "::warning title=iOS unsigned build::No iOS distribution signing configured (ios_signing.json Signed=false); NOT producing an .ipa. Add APPLE_DISTRIBUTION_CERT_* + IOS_PROVISION_* secrets (see .github/DEPLOYMENT.md) to build a store-uploadable .ipa.";
	}

	# publish info sidecar — MUST be "<title>-ios.json" (the app's update/deprecation check and the
	# "<title>-<dist>.json" convention shared with Android; see PublishAndroidApp.ps1). The .ipa is
	# attached to the GitHub release for archival/TestFlight; end users update through the App Store,
	# so InstallationPageUrl points at the store/download page rather than the .ipa.
	$json = [ordered]@{
		Version = $versionParam;
		UpdateInfoUrl = "$repoUrl/releases/latest/download/$module_infoFileName";
		PackageUrl = "$repoUrl/releases/download/$versionTag/$module_packageFileName";
		PackageId = "$packageId";
		InstallationPageUrl = "$installationPageUrl";
		ReleaseDate = "$releaseDate";
		DeprecatedVersion = "$deprecatedVersion";
		NotificationDelay = "$versionNotificationDelay";
	};
	$json | ConvertTo-Json | Out-File $module_infoFile -Encoding ASCII;

	if ($isLatest) {
		Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse;
	}

	ReportVersion
}
finally {
	Get-ChildItem -Path (Join-Path $projectDir "obj") -Recurse -File -Filter "*.tmp.csproj.nuget.*" -ErrorAction SilentlyContinue | Remove-Item -Force;
}

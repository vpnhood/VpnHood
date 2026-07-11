# Publishes the NuGet packages of a MODULE repo — a separate vpnhood library repo (e.g.
# VpnHood.Core.Proxies) that ships its own NuGets on its own cadence but stays version-aligned with
# the monorepo. Runs in the module repo's CI via the reusable workflow
# .github/workflows/publish_module_nugets.yml (this script rides the monorepo checkout); can also
# run locally from a sibling checkout as a -noPush dry run. See pub/RELEASE-STRATEGY.md.
#
# Version rule:
#   1. Read the monorepo's published version — ALWAYS from `develop` (develop always carries the
#      highest version; `main` only advances on a stable bump).
#   2. Monorepo ahead of the module's pub/PubVersion.json -> ADOPT the monorepo version (keeps the
#      family aligned). Otherwise -> bump the module's own build number (the module ran ahead; the
#      next monorepo bump leapfrogs it and re-syncs).
#   3. The published version is a stable `X.Y.Z` — the same rule as the monorepo's NuGets
#      (RELEASE-STRATEGY.md: "NuGet is always a stable Release version"; prerelease lines are an
#      APP concept). The -prerelease switch below is a MANUAL-ONLY escape hatch; nothing in the
#      normal flow sets it. The branch only determines where the bump commit is pushed.
# The new version is stamped into the module's pub/PubVersion.json (+ root Directory.Build.props)
# and committed back to the published branch BEFORE packing — CI owns the bump, exactly like the
# monorepo's bump.yml (a failed pack burns a cheap version number; an unrecorded bump would make
# the next run silently skip-duplicate).

param(
	# Root of the module repo to publish (NOT the monorepo checkout this script lives in).
	[Parameter(Mandatory = $true)][string]$moduleDir,
	# Branch the bump commit is pushed to. CI passes it explicitly (its checkout is detached);
	# locally it defaults to the module's current branch.
	[string]$branch,
	# MANUAL-ONLY escape hatch: publish `X.Y.Z-prerelease` instead of the stable version — for
	# letting a consumer try a build before the real release. The normal flow never sets it.
	[switch]$prerelease,
	# Monorepo version source. The raw develop URL keeps "always read develop" true no matter where
	# this runs (publishing needs network anyway); a local file path also works (tests/offline).
	[string]$vhVersionSource = "https://raw.githubusercontent.com/vpnhood/VpnHood/develop/pub/PubVersion.json",
	# Dry run: stamp the local version files and pack, but no commit/push and no nuget push.
	[switch]$noPush
);

$ErrorActionPreference = "Stop";

$moduleDir = (Resolve-Path $moduleDir).Path;
$versionFile = Join-Path $moduleDir "pub/PubVersion.json";
if (!(Test-Path $versionFile)) { throw "PublishModuleNugets: $versionFile not found — is $moduleDir an onboarded module repo?"; }

if ([string]::IsNullOrWhiteSpace($branch)) { $branch = git -C $moduleDir branch --show-current; }
if ([string]::IsNullOrWhiteSpace($branch)) { throw "PublishModuleNugets: cannot resolve the branch (detached HEAD?); pass -branch."; }

# Adopt the monorepo develop version when it is ahead; otherwise self-bump the build number.
$vhVersionJson = if (Test-Path $vhVersionSource) { Get-Content $vhVersionSource | ConvertFrom-Json } else { Invoke-RestMethod $vhVersionSource };
$vhVersion = [version]$vhVersionJson.Version;
$versionJson = Get-Content $versionFile | ConvertFrom-Json;
$moduleVersion = [version]$versionJson.Version;
$version = if ($vhVersion -gt $moduleVersion) { $vhVersion } else { [version]::new($moduleVersion.Major, $moduleVersion.Minor, $moduleVersion.Build + 1) };
$versionJson.Version = $version.ToString(3);
$versionJson.BumpTime = [datetime]::UtcNow.ToString("o");
$versionJson | ConvertTo-Json -Depth 10 | Out-File $versionFile;

$reason = if ($vhVersion -gt $moduleVersion) { "adopted monorepo $vhVersion" } else { "self-bump; monorepo develop is $vhVersion" };
Write-Host "Module version: $moduleVersion -> $($version.ToString(3)) ($reason)" -ForegroundColor Blue;

# Mirror into the module's root Directory.Build.props — the single <Version> its projects inherit
# (same pattern as the monorepo's src/Directory.Build.props stamp in VersionBump.ps1).
$propsFile = Join-Path $moduleDir "Directory.Build.props";
if (Test-Path $propsFile) {
	$props = Get-Content $propsFile -Raw;
	$props = ([regex]"<Version>.*?</Version>").Replace($props, "<Version>$($versionJson.Version)</Version>", 1);
	Set-Content -Path $propsFile -Value $props -Encoding utf8 -NoNewline;
}

# Stable by default (monorepo rule); -prerelease is the manual-only escape hatch. A prerelease
# publish still commits its bump, so the next stable publish simply takes the next build number —
# versions stay monotonic and can never collide.
$suffix = if ($prerelease) { "-prerelease" } else { "" };
$versionTag = "v$($version.ToString(3))$suffix";
$nugetVersion = "$($version.ToString(3))$suffix";

# Record the bump on the published branch before packing (CI-owned bump, like the monorepo).
if ($noPush) {
	Write-Host "noPush set: skipping the bump commit." -ForegroundColor Yellow;
}
else {
	git -C $moduleDir add -- "pub/PubVersion.json";
	if (Test-Path $propsFile) { git -C $moduleDir add -- "Directory.Build.props"; }
	git -C $moduleDir commit -m "Publish $versionTag";
	if ($LASTEXITCODE -ne 0) { throw "git commit failed (exit $LASTEXITCODE)"; }
	git -C $moduleDir push origin "HEAD:$branch";
	if ($LASTEXITCODE -ne 0) { throw "git push to $branch failed (exit $LASTEXITCODE)"; }
}

# Discover packable projects: a project IS a package unless it opts out with IsPackable=false —
# the same convention as the monorepo's PublishNugets.ps1. Module repos are small (a handful of
# projects), so a plain sequential pack loop replaces the monorepo's one-pass throwaway solution.
$projectFiles = Get-ChildItem -Path $moduleDir -Recurse -File -Filter "*.csproj" |
	Where-Object { $_.FullName -notmatch "[\\/](bin|obj)[\\/]" } |
	Where-Object { [System.IO.File]::ReadAllText($_.FullName) -notmatch "(?i)<IsPackable>\s*false\s*</IsPackable>" } |
	Sort-Object FullName;
if (@($projectFiles).Count -eq 0) { throw "PublishModuleNugets: no packable project found under $moduleDir."; }

$packDir = Join-Path $moduleDir "pub/bin/nuget";
Remove-Item $packDir -Recurse -Force -ErrorAction Ignore;
New-Item -ItemType Directory -Path $packDir -Force | Out-Null;

Write-Host "Packing $(@($projectFiles).Count) packable project(s) as $nugetVersion -> $packDir" -ForegroundColor Cyan;
foreach ($projectFile in $projectFiles) {
	dotnet pack $projectFile.FullName -c Release -o $packDir `
		-p:Version=$nugetVersion -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg -p:SolutionDir=$moduleDir;
	if ($LASTEXITCODE -gt 0) { throw "dotnet pack failed for $($projectFile.Name) (exit $LASTEXITCODE)."; }
}

if ($noPush) {
	Write-Host "noPush set: skipping nuget push (packages left in $packDir)." -ForegroundColor Yellow;
	return;
}

# NuGet key: CI injects NUGET_API_KEY; locally fall back to the .user file beside the repos.
$nugetApiKey = if ($env:NUGET_API_KEY) { $env:NUGET_API_KEY } elseif (Test-Path "$PSScriptRoot/../../../.user/nuget_api_key.txt") { "$(Get-Content "$PSScriptRoot/../../../.user/nuget_api_key.txt" -Raw)".Trim() } else { "" };
if ([string]::IsNullOrWhiteSpace($nugetApiKey)) {
	throw "PublishModuleNugets: NuGet API key is missing. Set the NUGET_API_KEY secret (CI) or .user/nuget_api_key.txt (local).";
}

# Push everything produced (pushing a .nupkg also pushes its adjacent .snupkg symbols).
$failed = @();
foreach ($pkg in Get-ChildItem -Path $packDir -File -Filter "*.nupkg") {
	dotnet nuget push $pkg.FullName --source "https://api.nuget.org/v3/index.json" --api-key $nugetApiKey --skip-duplicate;
	if ($LASTEXITCODE -gt 0) { $failed += $pkg.Name; Write-Host "push failed: $($pkg.Name)" -ForegroundColor Red; }
}
if ($failed.Count -gt 0) { throw "PublishModuleNugets: $($failed.Count) package push(es) failed: $($failed -join ', ')"; }

Write-Host "Published $versionTag" -ForegroundColor Green;

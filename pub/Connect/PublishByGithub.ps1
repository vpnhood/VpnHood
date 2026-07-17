# Releases VpnHood CONNECT. Connect's code + version live in THIS monorepo, but Connect releases to a
# SEPARATE repo (vpnhood/Vpnhood.App.Connect) whose workflow checks out this code at build time.
#
# Two steps, one command:
#   1. Bump the MONOREPO in CI (bump.yml) with client-publish AND nuget OFF — so PubVersion.json +
#      CHANGELOG advance and are pushed to develop + main. Waits for it to finish.
#   2. Dispatch connect_publish.yml in the CONNECT repo (ref = develop) to build Connect from the
#      freshly bumped code and release it there. No bump and no NuGet happen in the Connect repo.
#
# Usage:
#   ./PublishByGithub.ps1                       # prompts for both, then bumps + releases Connect
#   ./PublishByGithub.ps1 -bump 1 -rollout 20   # stable release staged to 20% on Google Play
#   ./PublishByGithub.ps1 -bump 2               # prerelease (alpha; rollout not asked)
#   ./PublishByGithub.ps1 -watch                # follow the Connect run to completion

param(
	# 1 = release, 2 = prerelease (alpha). 0 (default) => prompt.
	[ValidateSet(0, 1, 2)] [int]$bump = 0,
	# Google Play audience ratio as a percent (1-100). 0 (default) => prompt (release only).
	[int]$rollout = 0,
	# The monorepo that holds bump.yml + the Connect code. Defaults to this repo's resolved slug.
	[string]$monoRepo,
	# The Connect release repo (holds connect_publish.yml + fastlane + secrets).
	[string]$connectRepo = "vpnhood/Vpnhood.App.Connect",
	# Follow the triggered Connect run in the console until it finishes.
	[switch]$watch,
	# Skip the final confirmation prompt (for non-interactive use).
	[switch]$yes
);

$ErrorActionPreference = "Stop";

# gh authenticates the dispatch from its own login (gh auth login / keyring) or an ambient
# GITHUB_TOKEN — no token file. Run `gh auth login` once if dispatch fails with a 401.

. "$PSScriptRoot/../lib/Resolve-PublishRepo.ps1";
if ([string]::IsNullOrWhiteSpace($monoRepo)) { $monoRepo = Resolve-PublishRepoSlug; }
if ([string]::IsNullOrWhiteSpace($monoRepo)) {
	throw "Could not resolve the monorepo. Set -monoRepo owner/name or VH_PUBLISH_REPO.";
}

# --- Mandatory prompt 1: release or prerelease -------------------------------------------------
if ($bump -notin @(1, 2)) {
	do {
		$ans = Read-Host "Release type - 1: release (stable), 2: prerelease (alpha)";
	} until ($ans -in @("1", "2"));
	$bump = [int]$ans;
}
$prerelease = ($bump -eq 2);

# --- Mandatory prompt 2: Google Play audience ratio (release only) -----------------------------
if ($prerelease) {
	$rollout = 100;
}
elseif ($rollout -lt 1 -or $rollout -gt 100) {
	$parsed = 0;
	$ans = Read-Host "Google Play audience ratio % (1-100, default 100)";
	if ([string]::IsNullOrWhiteSpace($ans)) { $rollout = 100; }
	elseif ([int]::TryParse($ans, [ref]$parsed) -and $parsed -ge 1 -and $parsed -le 100) { $rollout = $parsed; }
	else { throw "Invalid audience ratio '$ans' (expected an integer 1-100)."; }
}

# Guard: both workflows must be indexed on their repos before they can be dispatched.
gh api "repos/$monoRepo/actions/workflows/bump.yml" --silent 2>$null | Out-Null;
if ($LASTEXITCODE -ne 0) { throw "GitHub has not indexed 'bump.yml' on $monoRepo yet (push a change to it first)."; }
gh api "repos/$connectRepo/actions/workflows/connect_publish.yml" --silent 2>$null | Out-Null;
if ($LASTEXITCODE -ne 0) { throw "GitHub has not indexed 'connect_publish.yml' on $connectRepo yet (push a change to it first)."; }

$releaseKind = if ($prerelease) { "prerelease (alpha)" } else { "release (stable/production)" };
$rolloutText = if ($prerelease) { "n/a (alpha ships complete)" } else { "$rollout%" };

Write-Host "";
Write-Host "*** Release Connect via GitHub Actions" -BackgroundColor Blue;
Write-Host "  1) bump monorepo : $monoRepo   (publish OFF, nuget OFF -> push develop + main)";
Write-Host "  2) publish Connect: $connectRepo   (build from monorepo develop, release there)";
Write-Host "  type             : $releaseKind";
Write-Host "  Play audience    : $rolloutText";
Write-Host "";

if (-not $yes) {
	$confirm = Read-Host "Proceed? (y/N)";
	if ($confirm -notin @("y", "Y", "yes", "YES")) {
		Write-Host "Aborted." -ForegroundColor Yellow;
		return;
	}
}

# --- Step 1: bump the monorepo (publish + nuget OFF), then wait for it to finish ----------------
Write-Host "Dispatching bump on $monoRepo ..." -ForegroundColor Cyan;
gh workflow run bump.yml `
	--repo $monoRepo `
	--ref develop `
	-f "prerelease=$($prerelease.ToString().ToLower())" `
	-f "then_publish=false" `
	-f "then_publish_nugets=false";
if ($LASTEXITCODE -ne 0) { throw "Failed to dispatch bump.yml on $monoRepo."; }

Start-Sleep -Seconds 6;
$bumpRun = (gh run list --repo $monoRepo --workflow bump.yml -L 1 --json databaseId --jq '.[0].databaseId');
if ([string]::IsNullOrWhiteSpace($bumpRun)) { throw "Could not find the queued bump run; check the Actions tab."; }
Write-Host "Waiting for the bump run ($bumpRun) to finish ..." -ForegroundColor Cyan;
gh run watch $bumpRun --repo $monoRepo --exit-status;
if ($LASTEXITCODE -ne 0) { throw "The bump run failed; Connect was NOT dispatched. Fix the bump, then retry."; }

# --- Step 2: dispatch the Connect release (build from the freshly bumped develop) -----------
Write-Host "Dispatching Connect release on $connectRepo ..." -ForegroundColor Cyan;
gh workflow run connect_publish.yml `
	--repo $connectRepo `
	--ref main `
	-f "ref=develop" `
	-f "build_android=true" `
	-f "publish_play=true" `
	-f "build_ios=true" `
	-f "publish_appstore=true" `
	-f "publish_release=true" `
	-f "rollout=$rollout";
if ($LASTEXITCODE -ne 0) { throw "Failed to dispatch connect_publish.yml on $connectRepo."; }
Write-Host "Dispatched. View runs: https://github.com/$connectRepo/actions/workflows/connect_publish.yml" -ForegroundColor Green;

if ($watch) {
	Start-Sleep -Seconds 6;
	$runId = (gh run list --repo $connectRepo --workflow connect_publish.yml -L 1 --json databaseId --jq '.[0].databaseId');
	if ([string]::IsNullOrWhiteSpace($runId)) {
		Write-Host "Could not find the queued Connect run yet; check the Actions tab." -ForegroundColor Yellow;
	} else {
		gh run watch $runId --repo $connectRepo --exit-status;
	}
}

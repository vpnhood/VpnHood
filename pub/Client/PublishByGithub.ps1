# Releases VpnHood! CLIENT. The client's code + version live in THIS monorepo, but the client is now
# PUBLISHED from a separate repo (vpnhood/Vpnhood.App.Client), whose workflow checks out this code at
# build time and supplies the fastlane tree + store secrets. Its GitHub release still lands back HERE
# — that repo sets VH_PUBLISH_REPO=vpnhood/VpnHood — so every existing download link, README badge and
# in-app update URL keeps working.
#
# Two steps, one command (the same shape as Connect's dispatcher beside this one):
#   1. Bump the MONOREPO in CI (bump.yml) with the nuget publish OFF — so PubVersion.json advances and
#      is pushed to develop + main. Waits for it to finish; a failed bump publishes nothing.
#   2. Dispatch publish_client.yml in the CLIENT repo (ref = develop) to build from the freshly bumped
#      code. No bump and no NuGet happen in the client repo.
#
# Why two dispatches instead of letting bump.yml chain the publish: bump.yml runs here, and dispatching
# a workflow in the client repo from there would need a cross-repo `actions: write` credential this
# repo deliberately does not hold. Your own gh login already has that reach locally, so the two-step
# costs no new credential and keeps this repo unable to trigger anything in a brand repo.
#
# Usage:
#   ./PublishByGithub.ps1                       # prompts for both, then bumps + releases the client
#   ./PublishByGithub.ps1 -bump 2               # prerelease (alpha; rollout not asked)
#   ./PublishByGithub.ps1 -bump 1 -rollout 20   # stable release staged to 20% on Google Play
#   ./PublishByGithub.ps1 -bump 2 -watch        # follow the client run to completion

param(
	# 1 = release, 2 = prerelease (alpha). 0 (default) => prompt.
	[ValidateSet(0, 1, 2)] [int]$bump = 0,
	# Google Play audience ratio as a percent (1-100). 0 (default) => prompt (release only).
	[int]$rollout = 0,
	# The monorepo that holds bump.yml, the version and the client code. Defaults to this repo's slug.
	[string]$monoRepo,
	# The client PUBLISH repo (holds publish_client.yml + fastlane + store secrets). Note this is not
	# where the release lands — that is the monorepo, per the client repo's VH_PUBLISH_REPO variable.
	[string]$clientRepo = "vpnhood/Vpnhood.App.Client",
	# Follow the triggered client run in the console until it finishes.
	[switch]$watch,
	# Skip the final confirmation prompt (for non-interactive use).
	[switch]$yes
);

$ErrorActionPreference = "Stop";

# gh authenticates the dispatch from its own login (gh auth login / keyring) or an ambient
# GITHUB_TOKEN — no token file. Run `gh auth login` once if dispatch fails with a 401.

# Resolve the monorepo the same way the build does (no side effects: this resolver does NOT bump the
# version, unlike Common.ps1, so it is safe to dot-source here).
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
gh api "repos/$clientRepo/actions/workflows/publish_client.yml" --silent 2>$null | Out-Null;
if ($LASTEXITCODE -ne 0) { throw "GitHub has not indexed 'publish_client.yml' on $clientRepo yet (push a change to it first)."; }

# The channel is DECLARED to publish_client.yml, which asserts it against the PubVersion.json the bump
# just wrote and refuses to build on disagreement. Deriving it from the same $prerelease that drives
# the bump is what keeps the two from ever drifting.
$releaseType = if ($prerelease) { "prerelease" } else { "release" };
$releaseKind = if ($prerelease) { "prerelease (alpha + TestFlight)" } else { "release (production + App Store)" };
$rolloutText = if ($prerelease) { "n/a (alpha ships complete)" } else { "$rollout%" };

Write-Host "";
Write-Host "*** Release VpnHood! CLIENT via GitHub Actions" -BackgroundColor Blue;
Write-Host "  1) bump monorepo : $monoRepo   (nuget OFF -> push develop + main)";
Write-Host "  2) publish client: $clientRepo   (build from monorepo develop)";
Write-Host "  GitHub release   : $monoRepo   (per the client repo's VH_PUBLISH_REPO)";
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

# --- Step 1: bump the monorepo (nuget OFF), then wait for it to finish --------------------------
Write-Host "Dispatching bump on $monoRepo ..." -ForegroundColor Cyan;
gh workflow run bump.yml `
	--repo $monoRepo `
	--ref develop `
	-f "prerelease=$($prerelease.ToString().ToLower())" `
	-f "then_publish_nugets=false";
if ($LASTEXITCODE -ne 0) { throw "Failed to dispatch bump.yml on $monoRepo."; }

Start-Sleep -Seconds 6;
$bumpRun = (gh run list --repo $monoRepo --workflow bump.yml -L 1 --json databaseId --jq '.[0].databaseId');
if ([string]::IsNullOrWhiteSpace($bumpRun)) { throw "Could not find the queued bump run; check the Actions tab."; }
Write-Host "Waiting for the bump run ($bumpRun) to finish ..." -ForegroundColor Cyan;
gh run watch $bumpRun --repo $monoRepo --exit-status;
if ($LASTEXITCODE -ne 0) { throw "The bump run failed; the client was NOT dispatched. Fix the bump, then retry."; }

# --- Step 2: dispatch the client release (build from the freshly bumped develop) ----------------
Write-Host "Dispatching client release on $clientRepo ..." -ForegroundColor Cyan;
gh workflow run publish_client.yml `
	--repo $clientRepo `
	--ref main `
	-f "ref=develop" `
	-f "release_type=$releaseType" `
	-f "build_android=true" `
	-f "publish_play=true" `
	-f "build_ios=true" `
	-f "publish_appstore=true" `
	-f "publish_release=true" `
	-f "rollout=$rollout";
if ($LASTEXITCODE -ne 0) { throw "Failed to dispatch publish_client.yml on $clientRepo."; }
Write-Host "Dispatched. View runs: https://github.com/$clientRepo/actions/workflows/publish_client.yml" -ForegroundColor Green;

if ($watch) {
	Start-Sleep -Seconds 6;
	$runId = (gh run list --repo $clientRepo --workflow publish_client.yml -L 1 --json databaseId --jq '.[0].databaseId');
	if ([string]::IsNullOrWhiteSpace($runId)) {
		Write-Host "Could not find the queued client run yet; check the Actions tab." -ForegroundColor Yellow;
	} else {
		gh run watch $runId --repo $clientRepo --exit-status;
	}
}

# Releases the VpnHood SERVER. The server's code + version live in THIS monorepo, but the server
# releases to a SEPARATE repo (vpnhood/VpnHood.App.Server) whose workflow checks out this code at
# build time — the same split as Connect (see pub/Connect/PublishByGithub.ps1). That release repo
# holds NO code and only a `main` branch (a develop line there would be meaningless); the
# develop -> main prerelease/stable model lives in THIS monorepo via bump.yml.
#
# Two steps, one command:
#   1. Bump the MONOREPO in CI (bump.yml) with client-publish AND nuget OFF — so PubVersion.json +
#      CHANGELOG advance and are pushed to develop (+ fast-forwarded to main on a STABLE bump).
#      Waits for it to finish.
#   2. Dispatch server_publish.yml in the SERVER repo (its own `main`) to build the server from the
#      freshly bumped monorepo code (ref = develop) and release it there — plus push the multi-arch
#      docker image. No bump and no NuGet happen in the server repo.
#
# Unlike Client/Connect there is NO app store and NO fastlane, so there is no rollout prompt.
#
# Usage:
#   ./PublishByGithub.ps1                 # prompts for release/prerelease, then bumps + releases the server
#   ./PublishByGithub.ps1 -bump 2         # prerelease (build from develop)
#   ./PublishByGithub.ps1 -bump 1         # stable release (bump fast-forwards main; build from develop)
#   ./PublishByGithub.ps1 -pushDocker:$false   # skip the Docker Hub push (GitHub release only)
#   ./PublishByGithub.ps1 -watch          # follow the server run to completion

param(
	# 1 = release, 2 = prerelease. 0 (default) => prompt.
	[ValidateSet(0, 1, 2)] [int]$bump = 0,
	# The monorepo that holds bump.yml + the server code. Defaults to this repo's resolved slug.
	[string]$monoRepo,
	# The server release repo (holds server_publish.yml + the Docker Hub secrets).
	[string]$serverRepo = "vpnhood/VpnHood.App.Server",
	# Push the multi-arch image to Docker Hub (needs DOCKERHUB_* secrets on $serverRepo). Default on.
	[bool]$pushDocker = $true,
	# Follow the triggered server run in the console until it finishes.
	[switch]$watch,
	# Skip the final confirmation prompt (for non-interactive use).
	[switch]$yes
);

$ErrorActionPreference = "Stop";

# gh authenticates the dispatch from its own login (gh auth login / keyring) or an ambient
# GITHUB_TOKEN — no token file. Run `gh auth login` once if dispatch fails with a 401.

. "$PSScriptRoot/../Lib/ResolvePublishRepo.ps1";
if ([string]::IsNullOrWhiteSpace($monoRepo)) { $monoRepo = Resolve-PublishRepoSlug; }
if ([string]::IsNullOrWhiteSpace($monoRepo)) {
	throw "Could not resolve the monorepo. Set -monoRepo owner/name or VH_PUBLISH_REPO.";
}

# --- Mandatory prompt: release or prerelease ---------------------------------------------------
if ($bump -notin @(1, 2)) {
	do {
		$ans = Read-Host "Release type - 1: release (stable), 2: prerelease";
	} until ($ans -in @("1", "2"));
	$bump = [int]$ans;
}
$prerelease = ($bump -eq 2);

# Guard: both workflows must be indexed on their repos before they can be dispatched.
gh api "repos/$monoRepo/actions/workflows/bump.yml" --silent 2>$null | Out-Null;
if ($LASTEXITCODE -ne 0) { throw "GitHub has not indexed 'bump.yml' on $monoRepo yet (push a change to it first)."; }
gh api "repos/$serverRepo/actions/workflows/server_publish.yml" --silent 2>$null | Out-Null;
if ($LASTEXITCODE -ne 0) { throw "GitHub has not indexed 'server_publish.yml' on $serverRepo yet (push it to the repo's default branch first)."; }

$releaseKind = if ($prerelease) { "prerelease" } else { "release (stable)" };
$dockerText = if ($pushDocker) { "yes (Docker Hub)" } else { "no (GitHub release only)" };

Write-Host "";
Write-Host "*** Release the Server via GitHub Actions" -BackgroundColor Blue;
Write-Host "  1) bump monorepo : $monoRepo   (publish OFF, nuget OFF -> push develop$(if (-not $prerelease) { ' + fast-forward main' }))";
Write-Host "  2) publish server: $serverRepo   (build from monorepo develop, release there)";
Write-Host "  type             : $releaseKind";
Write-Host "  push docker      : $dockerText";
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
if ($LASTEXITCODE -ne 0) { throw "The bump run failed; the server was NOT dispatched. Fix the bump, then retry."; }

# --- Step 2: dispatch the server release (build from the freshly bumped develop) ---------------
Write-Host "Dispatching server release on $serverRepo ..." -ForegroundColor Cyan;
gh workflow run server_publish.yml `
	--repo $serverRepo `
	--ref main `
	-f "ref=develop" `
	-f "publish_release=true" `
	-f "push_docker=$($pushDocker.ToString().ToLower())";
if ($LASTEXITCODE -ne 0) { throw "Failed to dispatch server_publish.yml on $serverRepo."; }
Write-Host "Dispatched. View runs: https://github.com/$serverRepo/actions/workflows/server_publish.yml" -ForegroundColor Green;

if ($watch) {
	Start-Sleep -Seconds 6;
	$runId = (gh run list --repo $serverRepo --workflow server_publish.yml -L 1 --json databaseId --jq '.[0].databaseId');
	if ([string]::IsNullOrWhiteSpace($runId)) {
		Write-Host "Could not find the queued server run yet; check the Actions tab." -ForegroundColor Yellow;
	} else {
		gh run watch $runId --repo $serverRepo --exit-status;
	}
}

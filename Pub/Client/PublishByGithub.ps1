# Triggers a FULL release on GitHub Actions by dispatching the "Bump Version" workflow (bump.yml),
# which is the single writer of the version: it bumps Pub/PubVersion.json (the CHANGELOG is
# hand-maintained, never rewritten), commits, fast-forwards develop + main, and then chains the
# client publish (Linux + Windows +
# Android + Google Play + GitHub release) AND the NuGet publish against the freshly bumped commit.
#
# Bumping runs in CI (not on a developer's machine) on purpose — it avoids version-file conflicts
# between developers. This script only prompts + dispatches; it never bumps locally.
#
# It asks two mandatory questions (unless supplied as parameters):
#   1. Release or prerelease            -> 1: release (stable/production), 2: prerelease (alpha)
#   2. Google Play audience ratio (%)   -> 1-100, default 100 (100 = full release; <100 stages on
#                                          the production track only — ignored on prerelease/alpha)
#
# Usage:
#   ./PublishByGithub.ps1                       # prompts for both, then releases
#   ./PublishByGithub.ps1 -bump 1 -rollout 20   # stable release staged to 20% on Google Play
#   ./PublishByGithub.ps1 -bump 2               # prerelease (rollout not asked; alpha ships complete)
#   ./PublishByGithub.ps1 -watch                # follow the bump run to completion
#   ./PublishByGithub.ps1 -repo owner/name      # override the target repo (else auto-resolved)

param(
	# 1 = release, 2 = prerelease (alpha). 0 (default) => prompt.
	[ValidateSet(0, 1, 2)] [int]$bump = 0,
	# Google Play audience ratio as a percent (1-100). 0 (default) => prompt (release only).
	[int]$rollout = 0,
	# owner/repo to dispatch against. Defaults to the same repo the rest of the pipeline publishes to
	# (VH_PUBLISH_REPO -> origin remote), via the shared resolver.
	[string]$repo,
	# Follow the triggered bump run in the console until it finishes.
	[switch]$watch,
	# Skip the final confirmation prompt (for non-interactive use).
	[switch]$yes
);

$ErrorActionPreference = "Stop";
$workflowFile = "bump.yml";

# gh authenticates the dispatch from its own login (gh auth login / keyring) or an ambient
# GITHUB_TOKEN — no token file. Run `gh auth login` once if dispatch fails with a 401.

# Resolve the target repo the same way the build does (no side effects: this resolver does NOT bump
# the version, unlike Common.ps1, so it is safe to dot-source here).
. "$PSScriptRoot/../Lib/ResolvePublishRepo.ps1";
if ([string]::IsNullOrWhiteSpace($repo)) {
	$repo = Resolve-PublishRepoSlug;
}
if ([string]::IsNullOrWhiteSpace($repo)) {
	throw "Could not resolve the target repo. Set -repo owner/name or VH_PUBLISH_REPO.";
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
# A staged rollout is a production concept; prerelease/alpha always ships complete to testers, so we
# do not ask for (or send) a ratio on a prerelease.
if ($prerelease) {
	$rollout = 100;
}
elseif ($rollout -lt 1 -or $rollout -gt 100) {
	$parsed = 0;
	$ans = Read-Host "Google Play audience ratio % (1-100, default 100)";
	if ([string]::IsNullOrWhiteSpace($ans)) {
		$rollout = 100;
	}
	elseif ([int]::TryParse($ans, [ref]$parsed) -and $parsed -ge 1 -and $parsed -le 100) {
		$rollout = $parsed;
	}
	else {
		throw "Invalid audience ratio '$ans' (expected an integer 1-100).";
	}
}

# Guard: GitHub only exposes a workflow for dispatch once it has indexed the file (which happens on
# the push that changes that file's contents). If the workflow file is on the branch but was never
# indexed, the dispatch 404s. Check up front and give an actionable message instead of a raw 404.
gh api "repos/$repo/actions/workflows/$workflowFile" --silent 2>$null | Out-Null;
if ($LASTEXITCODE -ne 0) {
	throw ("GitHub has not indexed '$workflowFile' on $repo, so it cannot be dispatched yet. " +
		"Make a commit that modifies .github/workflows/$workflowFile (a comment line is enough) " +
		"and push it to the default branch, then retry.");
}

$releaseKind = if ($prerelease) { "prerelease (alpha)" } else { "release (stable/production)" };
$rolloutText = if ($prerelease) { "n/a (alpha ships complete)" } else { "$rollout%" };

Write-Host "";
Write-Host "*** Trigger FULL release via the Bump workflow" -BackgroundColor Blue;
Write-Host "  repo             : $repo";
Write-Host "  type             : $releaseKind";
Write-Host "  Play audience    : $rolloutText";
Write-Host "  will bump version, then publish client (all platforms + Google Play) AND NuGet packages.";
Write-Host "";

if (-not $yes) {
	$confirm = Read-Host "Proceed? (y/N)";
	if ($confirm -notin @("y", "Y", "yes", "YES")) {
		Write-Host "Aborted." -ForegroundColor Yellow;
		return;
	}
}

gh workflow run $workflowFile `
	--repo $repo `
	--ref develop `
	-f "prerelease=$($prerelease.ToString().ToLower())" `
	-f "rollout=$rollout" `
	-f "then_publish=true" `
	-f "then_publish_nugets=true";

if ($LASTEXITCODE -ne 0) {
	throw "Failed to dispatch '$workflowFile'. Exit code: $LASTEXITCODE";
}
Write-Host "Dispatched. View runs: https://github.com/$repo/actions/workflows/$workflowFile" -ForegroundColor Green;

if ($watch) {
	# The run id is not returned by `workflow run`; give GitHub a moment to register the queued run,
	# then grab the newest run for this workflow and follow it.
	Start-Sleep -Seconds 6;
	$runId = (gh run list --repo $repo --workflow $workflowFile -L 1 --json databaseId --jq '.[0].databaseId');
	if ([string]::IsNullOrWhiteSpace($runId)) {
		Write-Host "Could not find the queued run yet; check the Actions tab." -ForegroundColor Yellow;
	} else {
		gh run watch $runId --repo $repo --exit-status;
	}
}

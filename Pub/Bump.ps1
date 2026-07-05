# The ONE place the version is bumped.
#
# Increments Pub/PubVersion.json (+ stamps Src/Directory.Build.props), commits, and fast-forwards
# BOTH development and main (no --force). Intended to run in CI (.github/workflows/bump.yml) so the
# bump never happens on a developer's machine — that avoids version-file conflicts between developers.
# Can also be run locally for a manual bump. See Pub/RELEASE-STRATEGY.md.
#
# The CHANGELOG is maintained BY HAND — this never rewrites it. Put the next release's notes under a
# leading "# Latest" heading; at release time CI extracts whatever the first H1 section is for the
# GitHub release note (Changelog_GetRecentSecion), and Google Play uses the same exclude-phrases pass.
#
# Usage:
#   ./Bump.ps1                 # stable release bump (x.y.Z + 1)
#   ./Bump.ps1 -bump 2         # prerelease bump
#   ./Bump.ps1 -noPush         # bump only, no commit/push (dry run)

param(
	# 1 = stable release, 2 = prerelease. Any value > 0 increments the build number.
	[int]$bump = 1,
	# Bump only but do not commit or push.
	[switch]$noPush
);

# The ONE version mutation: increment PubVersion.json + stamp Src/Directory.Build.props. Done here
# (not in Common.ps1) so Common stays a read-only environment load. Runs BEFORE Common so Common then
# reads the freshly bumped version. A 0/absent $bump is a no-op (VersionBump only mutates when > 0).
& "$PSScriptRoot/Lib/VersionBump.ps1" -versionFile "$PSScriptRoot/PubVersion.json" -bump $bump;

. "$PSScriptRoot/Lib/Common.ps1"

Write-Host "Bumped to v$versionParam (prerelease=$prerelease)" -ForegroundColor Green;

if ($noPush) {
	Write-Host "noPush set: skipping commit/push." -ForegroundColor Yellow;
	return;
}

# Commit the version + changelog, then fast-forward development and main WITHOUT --force. A forced
# rewrite of main breaks every fork/clone that tracks it; a non-fast-forward rejection here signals a
# real divergence to reconcile by hand rather than overwrite.
git --git-dir=$gitDir --work-tree=$solutionDir add -A;
git --git-dir=$gitDir --work-tree=$solutionDir commit -m "Publish $versionTag";
if ($LASTEXITCODE -ne 0) { throw "git commit failed (exit $LASTEXITCODE)"; }

git --git-dir=$gitDir --work-tree=$solutionDir push origin HEAD:development;
if ($LASTEXITCODE -ne 0) { throw "git push to development failed (exit $LASTEXITCODE)"; }

git --git-dir=$gitDir --work-tree=$solutionDir push origin HEAD:main;
if ($LASTEXITCODE -ne 0) { throw "git push to main failed (non-fast-forward? reconcile by hand, do not force) (exit $LASTEXITCODE)"; }

# Compatibility aliases for release assets that were RENAMED, so a link minted against the OLD name
# keeps resolving for a grace period after the rename.
#
# GitHub serves a release asset strictly by file name, so the moment a name changes every existing
# "…/releases/latest/download/<old-name>" URL starts returning "Not Found". Nothing goes red: the
# release publishes fine, and only the consumer on the other end of the stale URL notices. Emitting the
# old name alongside the new one for a few months gives those consumers time to move.
#
# THE ONE RENAME SO FAR — the Android arm64 web build, shipped as "arm64-web" up to v7.9.812 (client) /
# v7.9.811 (connect) and as "web-arm64" from v8.1.838 / v8.1.847 onward:
#     VpnHood<App>-android-arm64-web.{apk,json}  ->  VpnHood<App>-android-web-arm64.{apk,json}
# The new spelling matches the "<platform>-<channel>-<arch>" order used by every other module dir.
#
# ONLY THE UPDATE-INFO JSON IS ALIASED, NOT THE PACKAGE. The alias is a POINTER, not a second copy of
# the release: it carries the CURRENT release's payload, so its PackageUrl already names the real
# (new-name) APK. Anything still on the old URL therefore downloads the latest build from its canonical
# location, and there is no reason to duplicate ~20 MB per release under a name we no longer publish.
#
# Note for whoever traces an actual update bug here: our OWN Android apps never polled these arm64
# files. Both the universal and the arm64 APK are built from the same Client/Connect .Android.Web
# project, whose AppConfigs.UpdateInfoUrl has always named "VpnHood<App>-Android-web.json" (verified in
# the shipped v7.9.811 APK). So the rename did not cut off in-app update notifications; this alias
# exists for third parties and forks that polled the per-arch file directly.
#
# THIS IS TEMPORARY. Past the expiry below the alias stops being written and stops being attached — no
# release changes shape, the extra file simply stops appearing. Delete this file and its two call sites
# (Publish-AndroidApp.ps1, Publish-GithubRelease.ps1) at that point.

# Grace period end. Set to three months after the connect rename shipped (v8.1.847, 2026-08-31).
$script:LegacyAssetAliasExpiry = [datetime]"2026-12-01";

# Renamed Android distributions, keyed by the CURRENT distribution name -> the retired one. The map is
# keyed on the distribution segment rather than the whole file name so it holds for any app title: a
# fork that sets publish.json PackageTitle gets the alias under its own title, which is what a fork
# that published the old layout needs.
$script:LegacyAndroidDistributions = @{ "web-arm64" = "arm64-web" };

# The retired update-info file name for an Android distribution, or $null when there is nothing to
# alias (no rename recorded, or the grace period is over). Callers treat $null as "skip" — see the
# header for why expiry needs no other bookkeeping.
function Get-LegacyAndroidInfoFileName {
    param(
        [Parameter(Mandatory = $true)][string]$packageFileTitle,
        [Parameter(Mandatory = $true)][string]$distribution)

    if ((Get-Date) -ge $script:LegacyAssetAliasExpiry) { return $null; }

    $legacyDistribution = $script:LegacyAndroidDistributions[$distribution];
    if ([string]::IsNullOrWhiteSpace($legacyDistribution)) { return $null; }

    return "$packageFileTitle-android-$legacyDistribution.json";
}

param(
	[Parameter(Mandatory=$true)][object]$windows,
	[Parameter(Mandatory=$true)][object]$linux,
	[Parameter(Mandatory=$true)][object]$android,
	[Parameter(Mandatory=$true)][object]$samples,
	[switch]$cleanall
);

# Build-only orchestrator: builds the client platforms locally for testing. It does NOT bump the
# version, distribute, or push. The version bump lives in CI (pub/Bump.ps1 via bump.yml), NuGet
# publishing is CI-only (publish_nugets.yml — never published locally), and the GitHub release is
# created by publish_client.yml. See pub/RELEASE-STRATEGY.md.
. "$PSScriptRoot/../Lib/Common.ps1"

$windows = $windows -eq "1";
$linux = $linux -eq "1";
$android = $android -eq "1";
$samples = $samples -eq "1";

# clean all
if ($cleanall) {
	& $msbuild $solutionDir /p:Configuration=Release /t:Clean /verbosity:$msverbosity;
}

# clean old release notes
Remove-Item "$packagesRootDir/$packageClientDirName/ReleaseNote.txt" -ErrorAction Ignore;

if ($windows) {
	& "$solutionDir/src/Apps/Client.Win.Web/_publish.ps1";
}

if ($linux) {
	& "$solutionDir/src/Apps/Client.Linux.Web/_publish.ps1";
}

if ($android) {
	& "$solutionDir/src/Apps/Client.Android.Google/_publish.ps1";
	& "$solutionDir/src/Apps/Client.Android.Web/_publish.ps1";
	& "$solutionDir/src/Apps/Client.Android.Web/_publish-arm64.ps1";
}

# update and push samples nugets
if ($samples) {
	& "$solutionDir/../VpnHood.App.Samples/UpdateAndPush.ps1";
}
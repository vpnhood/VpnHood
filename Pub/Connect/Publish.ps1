param(
	[Parameter(Mandatory=$true)][object]$windows,
	[Parameter(Mandatory=$true)][object]$linux,
	[Parameter(Mandatory=$true)][object]$android,
	[switch]$cleanall
	);

# Build-only orchestrator: builds the Connect platforms locally for testing. It does NOT bump the
# version, distribute, or push. The version bump lives in CI (Pub/Bump.ps1 via bump.yml) and the
# GitHub release is created by connect_publish.yml in the Connect release repo. See Pub/RELEASE-STRATEGY.md.
. "$PSScriptRoot/../Lib/Common.ps1"

$windows = $windows -eq "1";
$linux = $linux -eq "1";
$android = $android -eq "1";

# clean all
if ($cleanall) {
	& $msbuild $solutionDir /p:Configuration=Release /t:Clean /verbosity:$msverbosity;
}

# Remove old ReleaseNote
Remove-Item "$packagesRootDir/$packageConnectDirName/ReleaseNote.txt" -ErrorAction Ignore;

if ($windows) {
	& "$solutionDir/Src/Apps/Connect.Win.Web/_publish.ps1";
}

if ($linux) {
	& "$solutionDir/Src/Apps/Connect.Linux.Web/_publish.ps1";
}

if ($android) {
	& "$solutionDir/Src/Apps/Connect.Android.Google/_publish.ps1";
	& "$solutionDir/Src/Apps/Connect.Android.Web/_publish.ps1";
	& "$solutionDir/Src/Apps/Connect.Android.Web/_publish-arm64.ps1";
}
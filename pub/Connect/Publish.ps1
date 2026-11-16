param(
	[Parameter(Mandatory=$true)][object]$windows,
	[Parameter(Mandatory=$true)][object]$linux,
	[Parameter(Mandatory=$true)][object]$android,
	[switch]$cleanall
	);

# Build-only orchestrator: builds the Connect platforms locally for testing. It does NOT bump the
# version, distribute, or push. The version bump lives in CI (pub/Invoke-VersionBump.ps1 via bump.yml) and the
# GitHub release is created by connect_publish.yml in the Connect release repo. See pub/RELEASE-STRATEGY.md.
. "$PSScriptRoot/../lib/Common.ps1"

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
	& "$solutionDir/src/apps/Connect/Connect.Windows.Web/_publish.ps1";
}

if ($linux) {
	& "$solutionDir/src/apps/Connect/Connect.Linux.Web/_publish.ps1";
}

if ($android) {
	& "$solutionDir/src/apps/Connect/Connect.Android.Google/_publish.ps1";
	& "$solutionDir/src/apps/Connect/Connect.Android.Web/_publish.ps1";
	& "$solutionDir/src/apps/Connect/Connect.Android.Web/_publish-arm64.ps1";
}
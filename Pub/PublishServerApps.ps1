param( 
	[Parameter(Mandatory=$true)][object]$bump,
	[Parameter(Mandatory=$true)][object]$distribute
	);

$distribute = $distribute -eq "1";

. "$PSScriptRoot/Common.ps1" -bump $bump;

# clean solution
& $msbuild "$solutionDir" /p:Configuration=Release /t:Clean /verbosity:$msverbosity;
$noclean = $true;

# publish server
	& "$solutionDir/VpnHood.Server.App.Net/Pub/publish_win.ps1";
	& "$solutionDir/VpnHood.Server.App.Net/Pub/publish_linux_x64.ps1";
	& "$solutionDir/VpnHood.Server.App.Net/Pub/publish_linux_arm64.ps1";
	& "$solutionDir/VpnHood.Server.App.Net/Pub/publish_docker.ps1" -distribute $distribute;

# upload
if ($distribute)
{
	& "$PSScriptRoot/PublishToGitHub.ps1";
}

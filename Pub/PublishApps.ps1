param( 
	[Parameter(Mandatory=$true)][object]$bump,
	[Parameter(Mandatory=$true)][object]$nugets,
	[Parameter(Mandatory=$true)][object]$client,
	[Parameter(Mandatory=$true)][object]$android, 
	[Parameter(Mandatory=$true)][object]$server,
	[Parameter(Mandatory=$true)][object]$distribute
	);

$nugets = $nugets -eq "1";
$android = $android -eq "1";
$distribute = $distribute -eq "1";
$client = $client -eq "1";
$server = $server -eq "1";

. "$PSScriptRoot/Common.ps1" -bump $bump

# clean all
& $msbuild "$solutionDir" /p:Configuration=Release /t:Clean /verbosity:$msverbosity;
$noclean = $true;
$noPushNuget = !$nugets

Remove-Item "$packagesRootDir/ReleaseNote.txt" -ErrorAction Ignore;

# rebuild libraries
& "$solutionDir/VpnHood.Common/_publish.ps1";
& "$solutionDir/VpnHood.Tunneling/_publish.ps1";

& "$solutionDir/VpnHood.Client/_publish.ps1";
& "$solutionDir/VpnHood.Client.Device.Android/_publish.ps1";
& "$solutionDir/VpnHood.Client.Device/_publish.ps1";
& "$solutionDir/VpnHood.Client.Device.WinDivert/_publish.ps1";
& "$solutionDir/VpnHood.Client.App/_publish.ps1";
& "$solutionDir/VpnHood.Client.App.UI/_publish.ps1";

& "$solutionDir/VpnHood.Server/_publish.ps1";
& "$solutionDir/VpnHood.Server.Access/_publish.ps1";

# publish client
if ($client)
{
	& "$solutionDir/VpnHood.Client.App.Win/_publish.ps1";
}

# publish server
if ($server)
{	
	& "$solutionDir/VpnHood.Server.App.Net/Pub/publish_win.ps1";
	& "$solutionDir/VpnHood.Server.App.Net/Pub/publish_linux.ps1";
	& "$solutionDir/VpnHood.Server.App.Net/Pub/publish_docker.ps1" -distribute $distribute;
}

# publish android
if ($android)
{	
	& "$solutionDir/VpnHood.Client.App.Android/_publish.ps1";
}

# distribute
if ($distribute)
{
	& "$PSScriptRoot/PublishToGitHub.ps1";
}

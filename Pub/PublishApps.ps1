param( 
	[Parameter(Mandatory=$true)][object]$bump,
	[Parameter(Mandatory=$true)][object]$nugets,
	[Parameter(Mandatory=$true)][object]$client,
	[Parameter(Mandatory=$true)][object]$android, 
	[Parameter(Mandatory=$true)][object]$server,
	[Parameter(Mandatory=$true)][object]$distribute
	);

$bump = $bump -eq "1";
$nugets = $nugets -eq "1";
$android = $android -eq "1";
$distribute = $distribute -eq "1";
$server = $server -eq "1";

. "$PSScriptRoot/Common.ps1" -bump:$bump;

# clean all
& $msbuild "$solutionDir" /p:Configuration=Release /t:Clean;
$noclean = $true;
$noPushNuget = !$nugets

Remove-Item "$packagesRootDir/ReleaseNote.txt" -ErrorAction Ignore;

# rebuild libraries
& "$solutionDir\VpnHood.Common\_publish.ps1"
& "$solutionDir\VpnHood.Tunneling\_publish.ps1"

& "$solutionDir\VpnHood.Client\_publish.ps1"
& "$solutionDir\VpnHood.Client.Device.Android\_publish.ps1"
& "$solutionDir\VpnHood.Client.Device\_publish.ps1"
& "$solutionDir\VpnHood.Client.Device.WinDivert\_publish.ps1"
& "$solutionDir\VpnHood.Client.App\_publish.ps1"
& "$solutionDir\VpnHood.Client.App.UI\_publish.ps1"

& "$solutionDir\VpnHood.Server\_publish.ps1"
& "$solutionDir\VpnHood.Server.Access\_publish.ps1"

# publish client
if ($client)
{
	Remove-Item $packagesClientDir -ErrorAction Ignore -Recurse;
	& "$solutionDir/VpnHood.Client.App.Win/_publish.ps1";
	& "$solutionDir/VpnHood.Client.App.Win.Setup/_publish.ps1";
}

# publish server
if ($server)
{	
	Remove-Item $packagesServerDir -ErrorAction Ignore -Recurse;
	& "$solutionDir/VpnHood.Server.App.Net/_publish.ps1" -ftp:$false;
}

if ($android)
{	
	& "$solutionDir/VpnHood.Client.App.Android/_publish.ps1";
}

# distribute
if ($distribute)
{
	& "$PSScriptRoot/PublishToGitHub.ps1";
}

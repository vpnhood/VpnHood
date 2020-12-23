param( 
	[Parameter(Mandatory=$true)][object]$bump,
	[Parameter(Mandatory=$true)][object]$distribute, 
	[Parameter(Mandatory=$true)][object]$server, 
	[Parameter(Mandatory=$true)][object]$ftp
	);

$bump = $bump -eq "1";
$distribute = $distribute -eq "1";
$server = $server -eq "1";
$ftp = $ftp -eq "1";

. "$PSScriptRoot/Common.ps1" -bump:$bump;

# clean all
& $msbuild "$solutionDir" /p:Configuration=Release /t:Clean;
$noclean = $true;

# publish client
Remove-Item "$packagesDir/client" -ErrorAction Ignore -Recurse;
# & "$solutionDir/VpnHood.Client.App.Win/_publish.ps1";
# & "$solutionDir/VpnHood.Client.App.Android/_publish.ps1";
# & "$solutionDir/VpnHood.Client.App.Win.Setup/_publish.ps1";

# publish server
if ($server)
{	
	Remove-Item "$packagesDir/server" -ErrorAction Ignore -Recurse;
	& "$solutionDir/VpnHood.Server.App.Net/_publish.ps1" -ftp:$ftp;
}

# distribute
if ($distribute)
{
	& "$PSScriptRoot/PublishToGitHub.ps1";
}

param( 
	[Parameter(Mandatory=$true)][AllowEmptyString()][string]$bump,
	[Parameter(Mandatory=$true)][AllowEmptyString()][string]$distribute, 
	[Parameter(Mandatory=$true)][AllowEmptyString()][string]$server, 
	[Parameter(Mandatory=$true)][AllowEmptyString()][string]$ftp
	);

if (!$bump) { $bump = $true; }
if (!$distribute) { $distribute = $true; }
if (!$server) { $server = $false; }
if (!$ftp) { $ftp = $true; }

$bump=[Boolean]::Parse($bump);
$distribute=[Boolean]::Parse($distribute);
$server=[Boolean]::Parse($server);
$ftp=[Boolean]::Parse($ftp);


. "$PSScriptRoot/Common.ps1" -bump:$bump;

# clean all
& $msbuild "$solutionDir" /p:Configuration=Release /t:Clean;
$noclean = $true;

# publish client
Remove-Item "$packagesDir/client" -ErrorAction Ignore -Recurse;
& "$solutionDir/VpnHood.Client.App.Win/_publish.ps1";
& "$solutionDir/VpnHood.Client.App.Android/_publish.ps1";
& "$solutionDir/VpnHood.Client.App.Win.Setup/_publish.ps1";

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

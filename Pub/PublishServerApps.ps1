param([switch]$bump, [switch]$dis, [switch]$ftp) 
. "$PSScriptRoot/Common.ps1" -bump:$bump;

# clean solution
& $msbuild "$solutionDir" /p:Configuration=Release /t:Clean;
$noclean = $true;

# publish server
Remove-Item "$packagesDir/*" -ErrorAction Ignore;
Remove-Item $packagesServerDir -ErrorAction Ignore -Recurse;
& "$solutionDir/VpnHood.Server.App.Net/_publish.ps1" -ftp:$ftp;

# upload
if ($dis)
{
	& "$PSScriptRoot/PublishToGitHub.ps1";
}

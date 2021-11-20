param( 
	[Parameter(Mandatory=$true)][object]$bump,
	[Parameter(Mandatory=$true)][object]$publish
	);

$bump = $bump -eq "1";
$publish = $publish -eq "1";

. "$PSScriptRoot\..\..\VpnHood\Pub\Common.ps1" -bump:$bump;

$packageName = "VpnHood-AccessServer";

. "$PSScriptRoot\..\..\VpnHood\Pub\PublishApp.ps1" `
	-projectDir $PSScriptRoot -withLauncher `


if ($publish)
{
	Write-Host "*** Pushing $packageId..." -BackgroundColor Yellow -ForegroundColor Black;

	. "$PSScriptRoot\_pub\InitServer.ps1" -server:$credentials.$packageId.Server `
		-user:$credentials.$packageId.User `
		-password:$credentials.$packageId.Password;
}

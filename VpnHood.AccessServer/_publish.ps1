param( 
	[Parameter(Mandatory=$true)][object]$bump,
	[Parameter(Mandatory=$true)][object]$ftp
	);

$bump = $bump -eq "1";
$ftp = $ftp -eq "1";

. "$PSScriptRoot\..\..\VpnHood\Pub\Common.ps1" -bump:$bump;

$packageName = "VpnHood-AccessServer";

. "$PSScriptRoot\..\..\VpnHood\Pub\PublishApp.ps1" `
	-projectDir $PSScriptRoot -withLauncher `
	-packageName $packageName `
	-ftp:$ftp

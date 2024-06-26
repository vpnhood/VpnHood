param( 
	[Parameter(Mandatory=$true)] [int]$bump,
	[Parameter(Mandatory=$true)] [bool][switch]$install_service,
	[Parameter(Mandatory=$true)] [string]$confirm_production_yes
) ;

if ($confirm_production_yes -ne "yes")
{
	throw "Please confirm that you want to publish to production by adding -confirm_production_yes";
	exit;
}

$projectDir = $PSScriptRoot;
$solutionDir = Split-Path -parent $projectDir;
$dataDir = (Split-Path -parent $solutionDir) + "\.user\access.vpnhood.com";
$secrets = (Get-Content "$dataDir\secrets.json" | Out-String | ConvertFrom-Json);

. "$solutionDir\pub\PublishService" `
	-AppName "VpnHoodAgent" `
	-remoteHost $secrets.ServerIp `
	-remoteUser $secrets.UserName `
	-configDir "$dataDir\configs" `
	-userPrivateKeyFile "$dataDir\ssh.openssh" `
	-executerFileName "vhagent" `
	-projectDir $projectDir `
	-bump $bump `
	-install_service $install_service;
	

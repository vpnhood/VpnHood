param( 
	[Parameter(Mandatory=$true)] [int]$bump,
	[Parameter(Mandatory=$true)] [bool][switch]$install_service 
) ;

$projectDir = $PSScriptRoot;
$solutionDir = Split-Path -parent $projectDir;
$dataDir = (Split-Path -parent $solutionDir) + "\.user\access-stage.vpnhood.com";
$secrets = (Get-Content "$dataDir\secrets.json" | Out-String | ConvertFrom-Json);

. "$solutionDir\pub\PublishService" `
	-AppName "VpnHoodAgent-stage" `
	-remoteHost $secrets.ServerIp `
	-remoteUser $secrets.UserName `
	-configDir "$dataDir\configs" `
	-userPrivateKeyFile "$dataDir\ssh.openssh" `
	-executerFileName "vhagent" `
	-projectDir $projectDir `
	-bump $bump `
	-install_service $install_service;
	

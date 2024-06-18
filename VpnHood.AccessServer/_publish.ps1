param( 
	[Parameter(Mandatory=$true)] [int]$bump,
	[Parameter(Mandatory=$true)] [bool][switch]$install_service 
) ;

$projectDir = $PSScriptRoot;
$solutionDir = Split-Path -parent $projectDir;
$dataDir = (Split-Path -parent $solutionDir) + "\.user\console-stage.vpnhood.com";
$secrets = (Get-Content "$dataDir\secrets.json" | Out-String | ConvertFrom-Json);

. "$solutionDir\pub\PublishService" `
	-AppName "VpnHoodConsole-stage" `
	-remoteHost $secrets.ServerIp `
	-remoteUser $secrets.UserName `
	-configDir "$dataDir\configs" `
	-userPrivateKeyFile "$dataDir\ssh.openssh" `
	-executerFileName "vhconsole" `
	-projectDir $projectDir `
	-bump $bump `
	-install_service $install_service;
	

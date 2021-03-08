# Initialize VpnHood Private Ubunto Server

 param( 
	[Parameter(Mandatory=$true)][string]$server,
	[Parameter(Mandatory=$true)][string]$user, 
	[Parameter(Mandatory=$true)][string]$password
	);

#$server="172.27.149.11";
#$user="user";
#$password="u";

$login="$user@$server";
$sudo="echo -e ""$password\\n"" | sudo -S";
$solutionDir = Split-Path -parent $PSScriptRoot;

$desDir="VpnHood.AccessServer.Install";
$zipPackage="$PSScriptRoot/../bin/Release/publish-pack/VpnHood-AccessServer.zip";


plink $login -pw $password -ssh -batch "mkdir $desDir -p";
pscp -r -pw $password -l $user "$PSScriptRoot/install.sh" "${login}:$desDir";
pscp -r -pw $password -l $user $zipPackage "${login}:$desDir/package.zip";
plink $login -pw $password -ssh -batch "cd $desDir && $sudo bash install.sh";

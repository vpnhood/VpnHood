param( 
	[Parameter(Mandatory=$true)][object]$bump,
	[Parameter(Mandatory=$true)][object]$docker,
	[Parameter(Mandatory=$true)][object]$distribute
	);

$distribute = $distribute -eq "1";
$docker = $docker -eq "1";

. "$PSScriptRoot/../Core/Common.ps1" -bump $bump

# remove old release note
Remove-Item "$packagesRootDir/$packageServerDirName/ReleaseNote.txt" -ErrorAction Ignore;

# copy install-msquic.sh to VpnHoodServer-linux-msquic.sh and remove \r
Write-Host "Copying msquic installer..." -ForegroundColor Green
$msquicSrc = "$solutionDir/Src/Apps/Server.Net/Pub/Linux/install-msquic.sh";
$msquicDst = "$packagesRootDir/$packageServerDirName/linux-any/VpnHoodServer-linux-msquic.sh";
(Get-Content $msquicSrc -Raw) -replace "`r", "" | Out-File -FilePath $msquicDst -Encoding ASCII -Force -NoNewline

& "$solutionDir/Src/Apps/Server.Net/_publish.ps1";

if ($docker) {
	& "$solutionDir/Src/Apps/Server.Net/Pub/publish_docker.ps1" -distribute $distribute;
}

# distribute
if ($distribute) {
    & "$PSScriptRoot/PublishToGitHub.ps1";
}

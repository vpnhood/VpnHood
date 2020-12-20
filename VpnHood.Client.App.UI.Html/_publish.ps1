$solutionDir = Split-Path -parent $PSScriptRoot;
. "$solutionDir\pub\Common.ps1";

$distDir= "$PSScriptRoot\dist";
$dest1= "$solutionDir\VpnHood.Client.App.Android\Assets\SPA.zip";
$dest2= "$solutionDir\VpnHood.Client.App.Win\Resources\SPA.zip";

# build output
& npm run build --prefix $PSScriptRoot
if ($LASTEXITCODE -gt 0) { Write-Host ("Error code: " + $lastexitcode) -ForegroundColor Red; pause;}

# zip
ZipFiles -Path $distDir -DestinationPath $dest1;
if ($LASTEXITCODE -gt 0) { Write-Host ("Error code: " + $lastexitcode) -ForegroundColor Red; pause;}

# Other projects
Copy-Item -path $dest1 -Destination $dest2 -Force
$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
$curDir = $PSScriptRoot;

# variables
$projectFile="$curDir/VpnHood.AppLib.Swagger.csproj";
$namespace = "VpnHood.Client.Api";
$nswagFile = "$curDir/Api/Api.nswag";
$outBaseFile = "VpnHood.Client.Api";
$noBuild = $false;

# calculated
$outputFile = "$curDir/Api/$outBaseFile.ts";

# run; nswag is a local dotnet tool, so it must run from the manifest root
$variables="/variables:namespace=$namespace,apiBaseFile=$outBaseFile,projectFile=$projectFile,nobuid=$noBuild";
Push-Location $SolutionDir;
try {
	dotnet tool restore;
	if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed."; }
	dotnet tool run nswag run $nswagFile $variables;
	if ($LASTEXITCODE -ne 0) { throw "nswag generation failed."; }
}
finally {
	Pop-Location;
}
# "/* eslint-disable */" + [Environment]::NewLine + (Get-Content $outputFile -Raw) | Set-Content $outputFile;

#copy to UI project if exists
$vhFolder = Split-Path -parent $SolutionDir;
$uiProjectTarget = "$vhFolder\VpnHood.Client.WebUI\src\services\VpnHood.Client.Api.ts";
if (Test-Path $uiProjectTarget) {
	copy-item $outputFile $uiProjectTarget -Force;
    Write-Host "Output has been copied to UI project. $uiProjectTarget";
} 
else{
	Write-Host "Could not update the UI project. $uiProjectTarget" -ForegroundColor Yellow;
}

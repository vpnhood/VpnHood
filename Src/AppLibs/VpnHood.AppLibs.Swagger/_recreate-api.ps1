$curDir = $PSScriptRoot;

# variables
$projectFile="$curDir/VpnHood.AppLibs.Swagger.csproj";
$namespace = "VpnHood.Core.Client.Api";
$nswagFile = "$curDir/Api/Api.nswag";
$outBaseFile = "VpnHood.Core.Client.Api";
$noBuild = $false;

# run
$nswagExe = "${Env:ProgramFiles(x86)}/Rico Suter/NSwagStudio/Net90/dotnet-nswag.exe";
$variables="/variables:namespace=$namespace,apiBaseFile=$outBaseFile,projectFile=$projectFile,nobuid=$noBuild";
& "$nswagExe" run $nswagFile $variables;

#copy to UI project if exists
$vhFolder = Split-Path (Split-Path -parent $curDir) -parent;
$uiProjectTarget = "$vhFolder\VpnHood.Core.Client.WebUI\src\services\VpnHood.Core.Client.Api.ts";
if (Test-Path $uiProjectTarget) {
	copy-item "$curDir/Api/$outBaseFile.ts" $uiProjectTarget -Force;
    Write-Host "Output has been copied to UI project. $uiProjectTarget";
} 
else{
	Write-Host "Could not update the UI project. $uiProjectTarget" -ForegroundColor Yellow;
}

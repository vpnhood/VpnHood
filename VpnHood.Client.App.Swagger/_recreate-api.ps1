$curDir = $PSScriptRoot;

# variables
$projectFile="$curDir/VpnHood.Client.App.Swagger.csproj";
$namespace = "VpnHood.Client.Api";
$nswagFile = "$curDir/Api/Api.nswag";
$outBaseFile = "VpnHood.Client.Api";
$noBuild = $false;

# run
$nswagExe = "${Env:ProgramFiles(x86)}/Rico Suter/NSwagStudio/Net80/dotnet-nswag.exe";
$variables="/variables:namespace=$namespace,apiBaseFile=$outBaseFile,projectFile=$projectFile,nobuid=$noBuild";
& "$nswagExe" run $nswagFile $variables;

#copy to UI project if exists
$vhFolder = Split-Path (Split-Path -parent $curDir) -parent;
$uiProjectTarget = "$vhFolder\VpnHood.Client.WebUI\src\services\VpnHood.Client.Api.ts";
if (Test-Path $uiProjectTarget) {
	copy-item "$curDir/Api/$outBaseFile.ts" $uiProjectTarget -Force;
    Write-Host "Output has been copied to UI project. $uiProjectTarget";
} 
else{
	Write-Host "Could not update the UI project. $uiProjectTarget" -ForegroundColor Yellow;
}

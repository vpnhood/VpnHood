$curDir = $PSScriptRoot;

# variables
$projectFile="$curDir/VpnHood.Client.App.Swagger.csproj";
$namespace = "VpnHood.Client.Api";
$nswagFile = "$curDir/Api/Api.nswag";
$outBaseFile = "VpnHood.Client.Api";
$noBuild = $false;

# run
$nswagExe = "${Env:ProgramFiles(x86)}/Rico Suter/NSwagStudio/Net70/dotnet-nswag.exe";
$variables="/variables:namespace=$namespace,apiBaseFile=$outBaseFile,projectFile=$projectFile,nobuid=$noBuild";
& "$nswagExe" run $nswagFile $variables /runtime:Net70;

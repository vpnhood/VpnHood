$curDir = $PSScriptRoot;
$solutionDir = (Split-Path $PSScriptRoot -Parent);

# variables
$projectFile="$solutionDir/VpnHood.AccessServer/VpnHood.AccessServer.csproj";
$nswagFile = "$curDir/Api/Api.nswag";
$namespace = "VpnHood.AccessServer.Api";
$outBaseFile = "VpnHood.AccessServer.Api";

# run
$nswagExe = "${Env:ProgramFiles(x86)}/Rico Suter/NSwagStudio/Net80/dotnet-nswag.exe";
$variables="/variables:namespace=$namespace,apiBaseFile=$outBaseFile,projectFile=$projectFile";
& "$nswagExe" run $nswagFile $variables;

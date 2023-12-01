$curDir = $PSScriptRoot;
$solutionDir = (Split-Path $PSScriptRoot -Parent);

# variables
$projectFile="$solutionDir/VpnHood.AccessServer/VpnHood.AccessServer.csproj";
$namespace = "VpnHood.AccessServer.Api";
$nswagFile = "$curDir/Api/Api.nswag";

# run
$nswagExe = "${Env:ProgramFiles(x86)}/Rico Suter/NSwagStudio/Net80/dotnet-nswag.exe";
$variables="/variables:namespace=$namespace,apiBaseFile=Api,projectFile=$projectFile";
& "$nswagExe" run $nswagFile $variables;

# fix beta generated code
# load the api.cs file 
$filePath = "$curDir/Api/Api.cs";
$fileContent = Get-Content -Path $filePath -Raw;
$fileContent = $fileContent -replace '"clientId:{clientId}"', '$"clientId:{clientId}"';
$fileContent | Set-Content -Path $filePath -Force;

$ErrorActionPreference = "Stop";
$curDir = $PSScriptRoot;
$publishInfoFile = "$curDir/publish.json";

# read publish info
$publishInfo = (Get-Content "$publishInfoFile" | Out-String | ConvertFrom-Json);
$exeFileR = $publishInfo.ExeFile;
$exeFile="$curDir/$exeFileR";

# Executing VpnHoodServer;
& "$exeFile" $args;
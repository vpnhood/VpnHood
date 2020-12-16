. "$PSScriptRoot\..\Pub\PublishApp.ps1" $PSScriptRoot -withLauncher
Copy-Item -path "$PSScriptRoot\run.vbs" -Destination "$publishDir\" -force
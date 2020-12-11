$solutionDir = Split-Path -parent $PSScriptRoot

& "$PSScriptRoot\publishNuget.ps1" -projectDir "$solutionDir\VpnHood.Common"
& "$PSScriptRoot\publishNuget.ps1" -projectDir "$solutionDir\VpnHood.Server.Access" 
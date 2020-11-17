$solutionDir = Split-Path -parent $PSScriptRoot

& "$PSScriptRoot\publishNuget.ps1" -projectDir "$solutionDir\VpnHood.Common.AppUpdater"
& "$PSScriptRoot\publishNuget.ps1" -projectDir "$solutionDir\VpnHood.Server.Common" 
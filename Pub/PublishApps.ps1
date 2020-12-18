. "$PSScriptRoot\Common.ps1"

# clean all
& $msbuild "$solutionDir" /p:Configuration=Release /t:Clean 
Remove-Item "$packagesDir\*" -ErrorAction Ignore -Recurse;

$noclean = $true

& "$solutionDir\VpnHood.Server.App.NetCore\_publish.ps1"
& "$solutionDir\VpnHood.Client.App.Win\_publish.ps1"
& "$solutionDir\VpnHood.Client.App.Android\_publish.ps1"
& "$solutionDir\VpnHood.Client.App.Win.Setup\_publish.ps1"

# upload server
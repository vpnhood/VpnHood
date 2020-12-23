param([switch]$fb) 
. "$PSScriptRoot\Common.ps1" -fb:$fb

# clean all
& $msbuild "$solutionDir" /p:Configuration=Release /t:Clean
$noclean = $true;

& "$solutionDir\VpnHood.Common\_publish.ps1"
& "$solutionDir\VpnHood.Tunneling\_publish.ps1"

& "$solutionDir\VpnHood.Client\_publish.ps1"
& "$solutionDir\VpnHood.Client.Device.WinDivert\_publish.ps1"
& "$solutionDir\VpnHood.Client.Device.Android\_publish.ps1"

& "$solutionDir\VpnHood.Client.App\_publish.ps1"
& "$solutionDir\VpnHood.Client.App.UI\_publish.ps1"

& "$solutionDir\VpnHood.Server\_publish.ps1"
& "$solutionDir\VpnHood.Server.Access\_publish.ps1"
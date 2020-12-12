. "$PSScriptRoot\Common.ps1"

# clean all
& $msbuild "$solutionDir" /p:Configuration=Release /t:Clean

& "$PSScriptRoot\publishNuget.ps1" -projectDir "$solutionDir\VpnHood.Common"
& "$PSScriptRoot\publishNuget.ps1" -projectDir "$solutionDir\VpnHood.Tunneling"

& "$PSScriptRoot\publishNuget.ps1" -projectDir "$solutionDir\VpnHood.Client"
& "$PSScriptRoot\publishNuget.ps1" -projectDir "$solutionDir\VpnHood.Client.Device.WinDivert"

& "$PSScriptRoot\publishNuget.ps1" -projectDir "$solutionDir\VpnHood.Client.App"
& "$PSScriptRoot\publishNuget.ps1" -projectDir "$solutionDir\VpnHood.Client.App.UI"

& "$PSScriptRoot\publishNuget.ps1" -projectDir "$solutionDir\VpnHood.Server" 
& "$PSScriptRoot\publishNuget.ps1" -projectDir "$solutionDir\VpnHood.Server.Access" 

# Special Project (Non dotnet build compatible)
& "$solutionDir\VpnHood.Client.Device.Android\_publish.ps1"

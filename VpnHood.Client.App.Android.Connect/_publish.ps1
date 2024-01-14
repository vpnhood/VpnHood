param([switch]$noapk)

& "$PSScriptRoot/../Pub/Core/PublishAndroidApp.ps1" $PSScriptRoot "VpnHoodConnect" -noapk:$noapk


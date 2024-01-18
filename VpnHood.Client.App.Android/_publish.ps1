param([switch]$apk)

& "$PSScriptRoot/../Pub/Core/PublishAndroidApp.ps1" $PSScriptRoot "VpnHoodClient" -apk:$apk;

param(
	# Pass-through to PublishWinApp.ps1: "all" (default, local), or "publish"/"package" for split CI steps.
	[Parameter(Mandatory=$false)] [ValidateSet("all", "publish", "package")] [String]$stage = "all"
)

$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/Pub/Core/PublishWinApp.ps1" `
	-projectDir $PSScriptRoot  `
	-appFolder "VpnHoodClient" `
	-aipFileR "Src/Apps/Client.Win.Web.Setup/VpnHood.App.Client.Win.Web.Setup.aip" `
	-distribution "web" `
	-installationPageUrl "https://www.vpnhood.com/vpnhood-client/download" `
	-stage $stage
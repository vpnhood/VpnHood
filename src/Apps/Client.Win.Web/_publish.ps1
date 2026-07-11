param(
	# Pass-through to PublishWinApp.ps1: "all" (default, local), or "publish"/"package" for split CI steps.
	[Parameter(Mandatory=$false)] [ValidateSet("all", "publish", "package")] [String]$stage = "all"
)

$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/pub/Lib/PublishWinApp.ps1" `
	-projectDir $PSScriptRoot  `
	-appFolder "VpnHoodClient" `
	-aipFileR "src/Apps/Client.Win.Web.Setup/VpnHood.App.Client.Win.Web.Setup.aip" `
	-distribution "web" `
	-stage $stage
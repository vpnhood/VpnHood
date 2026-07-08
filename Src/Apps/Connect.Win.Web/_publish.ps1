param(
	# Pass-through to PublishWinApp.ps1: "all" (default, local), or "publish"/"package" for split CI steps.
	[Parameter(Mandatory=$false)] [ValidateSet("all", "publish", "package")] [String]$stage = "all"
)

$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/Pub/Lib/PublishWinApp.ps1" `
	-projectDir $PSScriptRoot  `
	-appFolder "VpnHoodConnect" `
	-aipFileR "Src/Apps/Connect.Win.Web.Setup/VpnHood.App.Connect.Win.Web.Setup.aip" `
	-distribution "web" `
	-connect `
	-stage $stage
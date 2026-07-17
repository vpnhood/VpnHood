param(
	# Pass-through to Publish-WinApp.ps1: "all" (default, local), or "publish"/"package" for split CI steps.
	[Parameter(Mandatory=$false)] [ValidateSet("all", "publish", "package")] [String]$stage = "all"
)

$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot));
& "$SolutionDir/pub/lib/Publish-WinApp.ps1" `
	-projectDir $PSScriptRoot  `
	-appFolder "VpnHoodConnect" `
	-aipFileR "src/Apps/Connect.Win.Web.Setup/VpnHood.App.Connect.Win.Web.Setup.aip" `
	-distribution "web" `
	-connect `
	-stage $stage
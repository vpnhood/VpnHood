param(
	# Pass-through to Publish-WinApp.ps1: "all" (default, local), or "publish"/"package" for split CI steps.
	[Parameter(Mandatory=$false)] [ValidateSet("all", "publish", "package")] [String]$stage = "all"
)

$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot)));
# legacyMsiUpgradeCode: the Advanced Installer MSI that releases before the NSIS setup shipped,
# which the setup removes first
& "$SolutionDir/pub/lib/Publish-WinApp.ps1" `
	-projectDir $PSScriptRoot  `
	-appFolder "VpnHoodConnect" `
	-legacyMsiUpgradeCode "{CD5160B9-4A66-4F81-A162-9BA69FC81313}" `
	-distribution "web" `
	-connect `
	-stage $stage
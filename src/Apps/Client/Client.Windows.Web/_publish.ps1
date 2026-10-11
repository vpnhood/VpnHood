param(
	# Pass-through to Publish-WinApp.ps1: "all" (default, local), or "publish"/"package" for split CI steps.
	[Parameter(Mandatory=$false)] [ValidateSet("all", "publish", "package")] [String]$stage = "all"
)

$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot)));
# legacyMsiUpgradeCode: the Advanced Installer MSI that releases before the NSIS setup shipped,
# which the setup removes first
& "$SolutionDir/pub/lib/Publish-WinApp.ps1" `
	-projectDir $PSScriptRoot  `
	-appFolder "VpnHoodClient" `
	-legacyMsiUpgradeCode "{38FCFAB0-A40C-4840-A9F9-B7A06A5BC552}" `
	-distribution "web" `
	-stage $stage
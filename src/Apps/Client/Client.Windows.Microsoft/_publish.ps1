# The head's MSIX, the app and its VPN host (../Client.Windows.VpnPlugin): pub/lib/Publish-WinStoreApp.ps1, with
# its switches - -store for the Microsoft Store's upload package.
param(
	[Parameter(Mandatory=$false)] [ValidateSet("Debug", "Release")] [String]$configuration,
	[Parameter(Mandatory=$false)] [String]$publisher,
	[Parameter(Mandatory=$false)] [int]$revision,
	[Parameter(Mandatory=$false)] [switch]$store
)

$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot)));
& "$SolutionDir/pub/lib/Publish-WinStoreApp.ps1" -projectDir $PSScriptRoot `
	-vpnHostProjectDir (Join-Path $PSScriptRoot "../Client.Windows.VpnPlugin") @PSBoundParameters;

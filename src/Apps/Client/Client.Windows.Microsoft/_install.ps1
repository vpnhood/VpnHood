# Installs the package _publish.ps1 built: pub/lib/Install-WinStoreApp.ps1.
param(
	[Parameter(Mandatory=$false)] [String]$packagePath
)

$SolutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot)));
& "$SolutionDir/pub/lib/Install-WinStoreApp.ps1" -projectDir $PSScriptRoot @PSBoundParameters;

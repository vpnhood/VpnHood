# Installs the package _publish.ps1 built, for whoever runs this, updating an installed one in place:
# an uninstall first can leave the VPN plug-in in Windows' "DeploymentInProgress, Servicing" state,
# which only a restart clears (the WinStore fork's lesson 14). The development certificate becomes
# trusted for packages on this machine (LocalMachine\TrustedPeople), which needs an administrator, so
# a run without one asks for elevation and runs again.
param(
	[Parameter(Mandatory=$false)] [String]$packagePath = ""
)

$ErrorActionPreference = "Stop";
$outDir = Join-Path $PSScriptRoot "bin/Publish-microsoft";
$cerPath = Join-Path $outDir "dev-certificate.cer";
if (-not $packagePath) {
	$packages = @(Get-ChildItem -Path $outDir -Filter "*.msix" -ErrorAction Ignore);
	if ($packages.Count -ne 1) { throw "Expected one package in $outDir, found $($packages.Count): run _publish.ps1 first." }
	$packagePath = $packages[0].FullName;
}

$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent());
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
	$process = Start-Process -FilePath (Get-Process -Id $PID).Path -Verb RunAs -Wait -PassThru -ArgumentList @(
		"-NoProfile", "-File", "`"$PSCommandPath`"", "-packagePath", "`"$packagePath`"");
	if ($process.ExitCode -ne 0) { throw "The elevated install failed or was declined (exit code $($process.ExitCode))." }
	return;
}

Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null;
Add-AppxPackage -Path $packagePath -ForceApplicationShutdown -ForceUpdateFromAnyVersion;

# the identity the package carries, against what Windows now has installed
Add-Type -AssemblyName System.IO.Compression.FileSystem;
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath);
try {
	$reader = [IO.StreamReader]::new($archive.GetEntry("AppxManifest.xml").Open());
	try { [xml]$manifest = $reader.ReadToEnd(); }
	finally { $reader.Dispose(); }
}
finally {
	$archive.Dispose();
}

$identity = $manifest.Package.Identity;
$installed = Get-AppxPackage -Name $identity.Name;
if (-not $installed -or "$($installed.Version)" -ne $identity.Version) {
	throw "Windows has $($installed.Version) of $($identity.Name) installed, not the package's $($identity.Version).";
}
Write-Host "Installed: $($installed.PackageFullName)" -ForegroundColor Green;

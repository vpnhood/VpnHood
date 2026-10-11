# The uninstaller, which the installer places beside the launcher as vhuninstall.ps1 and names in
# Windows' installed apps. The storage - settings, certificates, logs - stays for a later install
# unless -purge.
$ErrorActionPreference = "Stop";
$productName = "$(productNameParam)";
$assemblyName = "$(assemblyNameParam)";
$launcher = "$(launcherNameParam)";
$jobName = $assemblyName;
$destinationPath = [IO.Path]::GetFullPath($PSScriptRoot);

# Run from installed apps, its window closes at the end: a failure waits there to be read
trap {
	Write-Output "Could not uninstall ${productName}: $_";
	if ($Host.Name -eq "ConsoleHost") { Read-Host "Press Enter" | Out-Null; }
	exit 1;
}

$purge = $false;
foreach ($arg in $args) {
	if ($arg -eq "-purge") { $purge = $true; }
	else { throw "Unknown argument! argument: $arg"; }
}

# Administrators only, as the install. Installed apps elevates it; run from a console that is not, it
# asks for the elevation itself, in a window that stays open to show how it went.
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent();
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
	$argumentList = @("-NoLogo", "-NoProfile", "-NoExit", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"") + $args;
	Start-Process "powershell.exe" -Verb RunAs -ArgumentList $argumentList;
	exit;
}

# Nothing is deleted but an install's folder, which has its publish.json
if (-not (Test-Path "$destinationPath/publish.json")) {
	throw "Not an install of ${productName}: $destinationPath";
}

Write-Output "Uninstalling $productName...";
# a current folder inside the install would hold it
Set-Location $env:SystemRoot;

# Stopped as the installer stops it: by its stop command, which removes its NAT and adapter. The task
# is disabled first, or its repetition starts it again; what does not stop is ended.
Disable-ScheduledTask -TaskName $jobName -ErrorAction Ignore | Out-Null;
# a native command's stderr is an error here
try { & "$destinationPath/$launcher.ps1" stop | Out-Null; }
catch { Write-Output "$jobName did not stop by its stop command: $_"; }
if (Get-ScheduledTask -TaskName $jobName -ErrorAction Ignore) {
	Start-Process "schtasks" "/end /tn $jobName" -Wait -NoNewWindow;
}
for ($i = 0; $i -lt 60; $i++) {
	$running = @(Get-Process -ErrorAction Ignore | Where-Object { $_.Path -and $_.Path.StartsWith("$destinationPath\", [StringComparison]::OrdinalIgnoreCase) });
	if ($running.Count -eq 0) { break; }
	Start-Sleep -Milliseconds 500;
}
$running | Stop-Process -Force -ErrorAction Continue;

foreach ($taskName in @($jobName, "${jobName}Updater")) {
	Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction Ignore;
}
Remove-Item -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$assemblyName" -Recurse -Force -ErrorAction Ignore;

if ($purge) {
	Remove-Item -Path $destinationPath -Recurse -Force;
}
else {
	Get-ChildItem -Path $destinationPath -Force | Where-Object { $_.Name -ne "storage" } | Remove-Item -Recurse -Force;
	if (-not (Get-ChildItem -Path $destinationPath -Force)) { Remove-Item -Path $destinationPath -Force; }
}
# the VpnHood folder above it, once nothing else is in it
$parentPath = Split-Path -Parent $destinationPath;
if (-not (Get-ChildItem -Path $parentPath -Force -ErrorAction Ignore)) { Remove-Item -Path $parentPath -Force -ErrorAction Ignore; }

Write-Output "$productName has been uninstalled.";
if (Test-Path "$destinationPath/storage") {
	Write-Output "  Its settings and data are kept for a later install: $destinationPath\storage";
	Write-Output "  To remove them too, delete $destinationPath";
}

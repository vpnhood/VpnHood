param(
	[Parameter(Mandatory = $true)] [string]$folder,
	# the setup's own process, which the app's updater runs from the install's updates folder
	[int]$exceptProcessId = 0,
	# The windows and trays it closes - uiExe in a person's session - written there, for the new service to open again
	# where they were (WindowsUiRelaunch): their session, and whether each was the tray alone.
	[string]$relaunchFile = "",
	[string]$uiExe = ""
)
# Ends every process run from the folder - a window, a tray, the service, an older install's app - so
# the installer can replace or remove its files. Chosen by the program's path, never its name, so
# another copy of the app elsewhere, a developer's build, keeps running. install-client.nsi runs it.
$ErrorActionPreference = "Stop";
$folder = [IO.Path]::GetFullPath($folder).TrimEnd('\') + '\';
$processes = @(Get-CimInstance Win32_Process |
	Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($folder, [StringComparison]::OrdinalIgnoreCase) -and
		$_.ProcessId -ne $exceptProcessId });

if ($relaunchFile) {
	$uis = @($processes | Where-Object { $_.SessionId -ne 0 -and $_.ExecutablePath -eq $uiExe } | ForEach-Object {
		@{ SessionId = $_.SessionId; IsTray = $_.CommandLine -match '\s--tray(\s|$)' } });
	# none open: an older list, which a service of a release before this one left unread, goes
	if ($uis.Count -gt 0) {
		ConvertTo-Json -InputObject $uis | Set-Content -Path $relaunchFile -Encoding UTF8;
	}
	else {
		Remove-Item $relaunchFile -Force -ErrorAction SilentlyContinue;
	}
}

foreach ($process in $processes) {
	Write-Output "Closing $($process.ExecutablePath) ($($process.ProcessId))";
	Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue;
}

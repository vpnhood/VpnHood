param([Parameter(Mandatory = $true)] [string]$folder)
# Ends every process run from the folder - a window, a tray, the service, an older install's app - so
# the installer can replace or remove its files. Chosen by the program's path, never its name, so
# another copy of the app elsewhere, a developer's build, keeps running. install-client.nsi runs it.
$ErrorActionPreference = "Stop";
$folder = [IO.Path]::GetFullPath($folder).TrimEnd('\') + '\';
Get-CimInstance Win32_Process |
	Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($folder, [StringComparison]::OrdinalIgnoreCase) } |
	ForEach-Object {
		Write-Output "Closing $($_.ExecutablePath) ($($_.ProcessId))";
		Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue;
	}

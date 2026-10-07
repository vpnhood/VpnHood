Write-Output "$(productNameParam) Installation for Windows";
$ErrorActionPreference = "Stop";

# Default arguments
$packageUrl="$(packageUrlParam)";
$versionTag="$(versionTagParam)";
$assemblyName="$(assemblyNameParam)";
$productName="$(productNameParam)";
$launcher="$(launcherNameParam)";

# Calculated path
$destinationPath = "${env:ProgramFiles}/VpnHood/$assemblyName";
$jobName=$assemblyName;
$packageFile = "";

for ($i = 0; $i -lt $args.length; $i++) {
	$arg = $args[$i];

	if ($arg -eq "-autostart") {
		$autostart = "y";
		$lastArg = ""; continue;
	}
	
	elseif ($arg -eq "-q") {
		$quiet = "y";
		$lastArg = ""; continue;
	}

	elseif ($lastArg -eq "-httpBaseUrl") {
		$httpBaseUrl = $arg;
		$lastArg = ""; continue;
	}
	elseif ($lastArg -eq "-httpAuthorization") {
		$httpAuthorization = $arg;
		$lastArg = ""; continue;
	}
	elseif ($lastArg -eq "-managementSecret") {
		$managementSecret = $arg;
		$lastArg = ""; continue;
	}
	elseif ($lastArg -eq "-packageFile") {
		$packageFile = $arg;
		$lastArg = ""; continue;
	}
	elseif ($lastArg -eq "-packageUrl") {
		$packageUrl = $arg;
		$lastArg = ""; continue;
	}
	elseif ($lastArg -eq "-versionTag") {
		$versionTag = $arg;
		$lastArg = ""; continue;
	}	
	elseif ("$lastArg" -ne "") {
		throw "Unknown argument! argument: $lastArg";
	}
	$lastArg = $arg;
}

# validate $versionTag
if ( "$versionTag" -eq "" ) {
	throw "Could not find versionTag!";
}

# User interaction
if ( "$quiet" -ne "y" ) {
	if ("$autostart" -eq "") { $autostart = Read-Host -Prompt "Auto Start (y/n)?" ; }
}

$binDir = "$destinationPath/bin/$versionTag";

# download & install. Into the install's own folder, not the current one: the updater's is System32,
# where every update used to leave the package.
New-Item -ItemType Directory -Path "$destinationPath/bin" -Force | Out-Null;
$downloadedPackageFile = "";
if ( "$packageFile" -eq "" ) {
	Write-Output "Downloading $assemblyName...";
	# .zip: Expand-Archive opens no other name
	$packageFile = "$destinationPath/bin/.package.zip";
	$downloadedPackageFile = $packageFile;
	[Net.ServicePointManager]::SecurityProtocol = "Tls, Tls11, Tls12";
	$oldProgressPreference = $ProgressPreference;
	try {
		$ProgressPreference = "SilentlyContinue";
		Invoke-RestMethod -ContentType "application/octet-stream" "$packageUrl" -OutFile "$packageFile";
	}
	finally {
		$ProgressPreference = $oldProgressPreference;
	}
}

# stopping the old service
Write-Output "Stopping $jobName (if any)...";
Start-Process "schtasks" "/end /tn $jobName";

# extracting
Write-Output "Extracting to $binDir";
Expand-Archive "$packageFile" -DestinationPath "$destinationPath/bin" -Force -ErrorAction Continue;
if ("$downloadedPackageFile" -ne "") { Remove-Item -Path "$downloadedPackageFile" -Force -ErrorAction Continue; }
# the package the updater of earlier releases left in its working folder, System32
Remove-Item -Path "${env:SystemRoot}/System32/$assemblyName-win.zip" -Force -ErrorAction Ignore;
# a reinstalled version keeps its folder's old time; the cleanup below goes by when a version was installed
(Get-Item "$binDir").LastWriteTime = Get-Date;

# Updating shared files...
Write-Output "Updating shared files...";
$infoDir = "$binDir/publish_info";
Copy-Item -path "$infoDir/vhupdate.ps1" -Destination "$destinationPath/" -Force;
Copy-Item -path "$infoDir/$launcher.ps1" -Destination "$destinationPath/" -Force;
Copy-Item -path "$infoDir/publish.json" -Destination "$destinationPath/" -Force;

# Old versions go only here, at an update: one stays while it is among the three newest, was
# installed in the last 30 days or a process still runs from it. Version folders at the top, from the
# layout before bin, count too; a folder whose name is not a whole version is left alone.
Write-Output "Removing old versions...";
$versionDirs = @(Get-ChildItem -Directory -Path "$destinationPath/bin", "$destinationPath" |
	Where-Object { $_.Name -match '^v\d+(\.\d+){1,3}(-.+)?$' });
$newestVersions = @($versionDirs |
	Sort-Object { [Version]($_.Name -replace '^v' -replace '-.*$') } -Descending |
	Select-Object -First 3 | ForEach-Object { $_.FullName });
# full paths, as the folders' are: a process started by a short (8.3) path reports that one
$runningFiles = @(Get-Process | ForEach-Object { $_.Path } | Where-Object { $_ } |
	ForEach-Object { try { [IO.Path]::GetFullPath($_) } catch { "" } });
foreach ($versionDir in $versionDirs) {
	$isRunning = @($runningFiles | Where-Object { $_.StartsWith("$($versionDir.FullName)\", [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0;
	if ($newestVersions -contains $versionDir.FullName -or $versionDir.LastWriteTime -gt (Get-Date).AddDays(-30) -or $isRunning) {
		continue;
	}
	Write-Output "Removing an old version: $($versionDir.FullName)";
	Remove-Item -Path $versionDir.FullName -Recurse -Force -ErrorAction Continue;
}

# Write AppSettings
if ("$httpBaseUrl" -ne "") {
	# publish info
	$appSettings = @{
		HttpAccessManager = @{
			BaseUrl       = $httpBaseUrl;
			Authorization = $httpAuthorization;
		};
		ManagementSecret = $managementSecret;
	};
	
	# publish info
	New-Item -ItemType Directory -Force -Path "$destinationPath/storage";
	$appSettings | ConvertTo-Json | Out-File "$destinationPath/storage/appsettings.json";
}

# AutoStart
if ($autostart -eq "y") {
	Write-Output "creating autostart service... Name: $jobName";
	$action = New-ScheduledTaskAction -Execute "$binDir/$assemblyName.exe";
	$trigger1 = New-ScheduledTaskTrigger -AtStartup;
	$trigger2 = New-ScheduledTaskTrigger -once -RepetitionInterval "00:01:00" -At (Get-Date);
	$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Seconds 0);
	$task = New-ScheduledTask -Action $action -Trigger @($trigger1, $trigger2) -Settings $settings;
	Register-ScheduledTask -User "System" -TaskName "$jobName" -InputObject $task -Force -AsJob | Out-Null;

	Write-Output "creating auto update service... Name: ${jobName}Updater";
	$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NonInteractive -NoLogo -NoProfile -File `"$destinationPath/vhupdate.ps1`" -q";
	$trigger = New-ScheduledTaskTrigger -Daily -At 3am;
	$task = New-ScheduledTask -Action $action -Trigger $trigger -Settings $settings;
	Register-ScheduledTask -User "System" -TaskName "${jobName}Updater" -InputObject $task -Force | Out-Null;

	Start-Process "schtasks" "/run /tn $jobName";
}
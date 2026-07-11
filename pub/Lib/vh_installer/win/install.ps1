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

$binDir = "$destinationPath/$versionTag";

# download & install
if ( "$packageFile" -eq "" ) {
	Write-Output "Downloading $assemblyName...";
	$packageFile = "$assemblyName-win.zip";
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
New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null;
Write-Output "Stopping $jobName (if any)...";
Start-Process "schtasks" "/end /tn $jobName";

# extracting
Write-Output "Extracting to $destinationPath";
Expand-Archive "$packageFile" -DestinationPath "$destinationPath" -Force -ErrorAction Continue;

# Updating shared files...
Write-Output "Updating shared files...";
$infoDir = "$binDir/publish_info";
Copy-Item -path "$infoDir/vhupdate.ps1" -Destination "$destinationPath/" -Force;
Copy-Item -path "$infoDir/$launcher.ps1" -Destination "$destinationPath/" -Force;
Copy-Item -path "$infoDir/publish.json" -Destination "$destinationPath/" -Force;

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
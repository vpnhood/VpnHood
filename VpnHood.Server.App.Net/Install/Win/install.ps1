Write-Output "VpnHood Server Installation for Windows";
$ErrorActionPreference = "Stop";

# Default arguments
$packageUrl = "$packageUrlParam";
$versionTag = "$versionTagParam";
$destinationPath = "${env:ProgramFiles}/VpnHood/VpnHoodServer";
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

	elseif ($lastArg -eq "-restBaseUrl") {
		$restBaseUrl = $arg;
		$lastArg = ""; continue;
	}
	elseif ($lastArg -eq "-restAuthorization") {
		$restAuthorization = $arg;
		$lastArg = ""; continue;
	}
	elseif ($lastArg -eq "-secret") {
		$secret = $arg;
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

# point to latest version if $packageUrl is not set
if ( "$packageUrl" -eq "" ) {
	$packageUrl = "https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodServer.zip";
}

# download & install VpnHoodServer
if ( "$packageFile" -eq "" ) {
	Write-Output "Downloading VpnHoodServer...";
	$packageFile = "VpnHoodServer-win.zip";
	[Net.ServicePointManager]::SecurityProtocol = "Tls, Tls11, Tls12";
	$oldProgressPreference = $ProgressPreference;
	try {
		$ProgressPreference = 'SilentlyContinue';
		Invoke-RestMethod -ContentType "application/octet-stream" "$packageUrl" -OutFile "$packageFile";
	}
	finally {
		$ProgressPreference = $oldProgressPreference;
	}
}

# stopping the old service
New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null;
Write-Output "Stopping VpnHoodServer (if any)...";
schtasks /end /tn "VpnHoodServer";

# extracting
Write-Output "Extracting to $destinationPath";
Expand-Archive "$packageFile" -DestinationPath "$destinationPath" -Force -ErrorAction Continue;

# Updating shared files...
Write-Output "Updating shared files...";
$infoDir = "$binDir/publish_info";
Copy-Item -path "$infoDir/vhupdate.ps1" -Destination "$destinationPath/" -Force;
Copy-Item -path "$infoDir/vhserver.ps1" -Destination "$destinationPath/" -Force;
Copy-Item -path "$infoDir/publish.json" -Destination "$destinationPath/" -Force;

# Write AppSettings
if ("$restBaseUrl" -ne "") {
	# publish info
	$appSettings = @{
		HttpAccessServer = @{
			BaseUrl       = $restBaseUrl;
			Authorization = $restAuthorization;
		};
		Secret = $secret;
	};
	
	# publish info
	$appSettings | ConvertTo-Json | Out-File "$destinationPath/appsettings.json";
}

# AutoStart VpnHood Server
if ($autostart -eq "y") {
	Write-Output "creating autostart service... Name: VpnHoodServer";
	$action = New-ScheduledTaskAction -Execute "$binDir/VpnHoodServer.exe";
	$trigger1 += New-ScheduledTaskTrigger -AtStartup;
	$trigger2 += New-ScheduledTaskTrigger -once -RepetitionInterval "00:01:00" -At (Get-Date);
	$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Seconds 0);
	$task = New-ScheduledTask -Action $action -Trigger @($trigger1, $trigger2) -Settings $settings;
	Register-ScheduledTask -User "System" -TaskName 'VpnHoodServer' -InputObject $task -Force -AsJob | Out-Null;

	Write-Output "creating auto update service... Name: VpnHoodUpdater";
	$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NonInteractive -NoLogo -NoProfile -File `"$destinationPath/vhupdate.ps1`" -q";
	$trigger = New-ScheduledTaskTrigger -Daily -At 3am;
	$task = New-ScheduledTask -Action $action -Trigger $trigger -Settings $settings;
	Register-ScheduledTask -User "System" -TaskName 'VpnHoodUpdater' -InputObject $task -Force | Out-Null;

	schtasks /run /tn "VpnHoodServer";
}
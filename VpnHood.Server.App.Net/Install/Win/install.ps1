echo "VpnHood Server Installation for Windows";
$ErrorActionPreference = "Stop";

# Default arguments
$packageUrl="$packageUrlParam";
$versionTag="$versionTagParam";
$destinationPath="${env:ProgramFiles}/VpnHood/VpnHoodServer";
$packageFile="";

for ($i=0; $i -lt $args.length; $i++)
{
	$arg = $args[$i];

    if ($arg -eq "-autostart") {
		$autostart="y";
		$lastArg=""; continue;
	}
	
	elseif ($arg -eq "-q") {
		$quiet="y";
		$lastArg=""; continue;
	}

	elseif ($lastArg -eq "-restBaseUrl") {
		$restBaseUrl=$arg;
		$lastArg=""; continue;
	}
	elseif ($lastArg -eq "-restAuthorization") {
		$restAuthorization=$arg;
		$lastArg=""; continue;
	}
	elseif ($lastArg -eq "-packageFile") {
		$packageFile=$arg;
		$lastArg=""; continue;
	}
	elseif ($lastArg -eq "-versionTag") {
		$versionTag=$arg;
		$lastArg=""; continue;
	}	
	elseif ("$lastArg" -ne "") {
		throw "Unknown argument! argument: $lastArg";
	}
	$lastArg=$arg;
}

# validate $versionTag
if ( "$versionTag" -eq "" ){
	throw "Could not find versionTag!";
}

# User interaction
if ( "$quiet" -ne "y" ){
	if ("$autostart" -eq "") { $autostart = Read-Host -Prompt "Auto Start (y/n)?" ; }
}

$binDir="$destinationPath/$versionTag";

# point to latest version if $packageUrl is not set
if ( "$packageUrl" -eq "" ){
	$packageUrl="https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodServer.zip";
}

# download & install VpnHoodServer
if ( "$packageFile" -eq "" ){
	echo "Downloading VpnHoodServer...";
	$packageFile="VpnHoodServer-win.zip";
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

# extract
echo "Extracting to $destinationPath";
New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null;
Expand-Archive "$packageFile" -DestinationPath "$destinationPath" -Force

# Updating shared files...
echo "Updating shared files...";
$infoDir="$binDir/publish_info";
Copy-Item -path "$infoDir/update.ps1" -Destination "$destinationPath/" -Force;
Copy-Item -path "$infoDir/vhserver.ps1" -Destination "$destinationPath/" -Force;
Copy-Item -path "$infoDir/publish.json" -Destination "$destinationPath/" -Force;

# AutoStart VpnHood Server
if ($autostart -eq "y")
{
	echo "creating autostart service... Name: VpnHoodServer";
	$action = New-ScheduledTaskAction -Execute "$binDir/VpnHoodServer.exe";
	$trigger = New-ScheduledTaskTrigger -AtStartup;
	$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Seconds 0);
	$settings.CimInstanceProperties.Item('MultipleInstances').Value = 3; #StopExisting
	$task = New-ScheduledTask -Action $action -Trigger $trigger -Settings $settings;
	Register-ScheduledTask -User "System" -TaskName 'VpnHoodServer' -InputObject $task -Force -AsJob | Out-Null;

	echo "creating auto update service... Name: VpnHoodUpdater";
	$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NonInteractive -NoLogo -NoProfile -File `"$destinationPath/update.ps1`"";
	$trigger = New-ScheduledTaskTrigger -Daily -At 3am;
	$task = New-ScheduledTask -Action $action -Trigger $trigger -Settings $settings;
	Register-ScheduledTask -User "System" -TaskName 'VpnHoodUpdater' -InputObject $task -Force | Out-Null;

	 schtasks /run /tn "VpnHoodServer";
}
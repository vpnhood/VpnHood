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
$autostart = "";
$autoupdate = "";

# The client protocols the server speaks, ServerHost's MinProtocolVersion and MaxProtocolVersion
$minProtocolVersion = 8;
$maxProtocolVersion = 15;

# Read arguments. The updater of an installed server runs this script too, with -q.
for ($i = 0; $i -lt $args.length; $i++) {
	$arg = $args[$i];

	if ($arg -eq "-noautostart") {
		$autostart = "n";
		$lastArg = ""; continue;
	}
	elseif ($arg -eq "-noautoupdate") {
		$autoupdate = "n";
		$lastArg = ""; continue;
	}
	elseif ($arg -eq "-q") {
		$quiet = "y";
		$lastArg = ""; continue;
	}
	# return, not exit: run through iex, exit would close the person's window
	elseif ($arg -in @("-help", "--help", "-?")) {
		Write-Output "Options:";
		Write-Output "  -q                    no questions: what is not given keeps the install's choices,";
		Write-Output "                        and a new install starts at boot and updates itself";
		Write-Output "  -noautostart          do not start the server at boot";
		Write-Output "  -noautoupdate         do not update the server by itself";
		Write-Output "  -httpBaseUrl <url>    the access server; with -httpAuthorization <value>";
		Write-Output "  -managementSecret <secret>";
		Write-Output "  -minClientProtocolVersion <n>";
		Write-Output "                        the oldest app protocol accepted, $minProtocolVersion-${maxProtocolVersion}: $minProtocolVersion, the oldest supported, keeps";
		Write-Output "                        the gate open to every app; $maxProtocolVersion resists probes, and older apps";
		Write-Output "                        cannot connect. Not with -httpBaseUrl: its server profile sets it";
		return;
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
	elseif ($lastArg -eq "-minClientProtocolVersion") {
		$minClientProtocolVersion = $arg;
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
		throw "Unknown argument! argument: $lastArg (-help lists the options)";
	}
	$lastArg = $arg;
}
# a last option still waiting for its value, or an unknown last one
if ("$lastArg" -ne "") {
	throw "Unknown argument or missing value! argument: $lastArg (-help lists the options)";
}

# validate $versionTag
if ( "$versionTag" -eq "" ) {
	throw "Could not find versionTag!";
}

# A choice no option made keeps the install's own, so an update - run with -q alone - changes none; a
# new install starts at boot and updates itself. Asked unless -q, with that as the answer Enter gives.
function Get-TaskChoice([string]$taskName) {
	$task = Get-ScheduledTask -TaskName $taskName -ErrorAction Ignore;
	if (-not (Test-Path "$destinationPath/publish.json") -or ($task -and $task.State -ne "Disabled")) { return "y"; }
	return "n";
}
function Read-Choice([string]$question, [string]$default) {
	$hint = if ($default -eq "n") { "y/N" } else { "Y/n" };
	$answer = "$(Read-Host -Prompt "$question ($hint)")".Trim().ToLower();
	if ($answer -eq "") { $answer = $default; }
	if ($answer.StartsWith("y")) { return "y"; }
	return "n";
}
$defaultAutostart = Get-TaskChoice $jobName;
$defaultAutoupdate = Get-TaskChoice "${jobName}Updater";
if ("$quiet" -ne "y") {
	if ($autostart -eq "") { $autostart = Read-Choice "Start the server at boot" $defaultAutostart; }
	if ($autoupdate -eq "") { $autoupdate = Read-Choice "Keep the server up to date by itself" $defaultAutoupdate; }
}
if ($autostart -eq "") { $autostart = $defaultAutostart; }
if ($autoupdate -eq "") { $autoupdate = $defaultAutoupdate; }

# The minimum client protocol, for a server that keeps its own access keys: a managed one takes it from
# its server profile, which a value here would override. Asked unless -q, with the settings' own as the
# answer Enter gives, else the oldest supported. Settled here, how it is written too, before anything
# changes.
$settingsFile = "$destinationPath/storage/appsettings.json";
$settingsText = if (Test-Path $settingsFile) { [IO.File]::ReadAllText($settingsFile) } else { "" };
$minClientProtocolWrite = "";
function Test-ProtocolVersion([string]$value) {
	return $value -match '^\d{1,3}$' -and [int]$value -ge $minProtocolVersion -and [int]$value -le $maxProtocolVersion;
}
if ("$minClientProtocolVersion" -ne "" -and -not (Test-ProtocolVersion $minClientProtocolVersion)) {
	throw "-minClientProtocolVersion takes a number from $minProtocolVersion to $maxProtocolVersion.";
}
if ("$httpBaseUrl" -ne "" -or $settingsText -match '"HttpAccessManager"') {
	if ("$minClientProtocolVersion" -ne "") {
		throw "A managed server takes its minimum client protocol version from its server profile.";
	}
}
else {
	$currentMinClientProtocolVersion = if ($settingsText -match '"MinClientProtocolVersion"\s*:\s*(\d+)') { $Matches[1] } else { "" };
	$defaultMinClientProtocolVersion = if ($currentMinClientProtocolVersion -ne "") { $currentMinClientProtocolVersion } else { "$minProtocolVersion" };
	if ("$quiet" -ne "y" -and "$minClientProtocolVersion" -eq "") {
		Write-Output "The oldest app protocol this server accepts. $minProtocolVersion, the oldest supported, keeps the gate open to";
		Write-Output "every app; $maxProtocolVersion resists probes, and apps older than it cannot connect.";
		while ($true) {
			$answer = "$(Read-Host -Prompt "Minimum client protocol version ($minProtocolVersion-$maxProtocolVersion, Enter for $defaultMinClientProtocolVersion)")".Trim();
			if ($answer -eq "") { $answer = $defaultMinClientProtocolVersion; }
			if (Test-ProtocolVersion $answer) { $minClientProtocolVersion = $answer; break; }
			Write-Output "Enter a number from $minProtocolVersion to $maxProtocolVersion.";
		}
	}
	# as a JSON number: 08 is none
	if ("$minClientProtocolVersion" -ne "") { $minClientProtocolVersion = "$([int]$minClientProtocolVersion)"; }
	# Written only when it changes. The settings' own key is replaced where it is. Into settings without
	# it, a ServerConfig goes first, which the server lays over its access manager's: a second
	# FileAccessManager would replace the person's whole. Settings with a ServerConfig of their own are
	# the person's to edit.
	if ("$minClientProtocolVersion" -ne "" -and "$minClientProtocolVersion" -ne $defaultMinClientProtocolVersion) {
		if ($currentMinClientProtocolVersion -ne "") { $minClientProtocolWrite = "replace"; }
		elseif (-not $settingsText.Contains('"')) { $minClientProtocolWrite = "new"; }
		elseif ($settingsText -notmatch '"ServerConfig"') { $minClientProtocolWrite = "insert"; }
		else {
			throw "Could not set the minimum client protocol version in $settingsFile, which has a ServerConfig of its own. Add `"MinClientProtocolVersion`": $minClientProtocolVersion to its Session.";
		}
	}
}

$binDir = "$destinationPath/bin/$versionTag";
$installFullPath = [IO.Path]::GetFullPath($destinationPath);

# Download and unpack while the running server goes on, as on Linux: a download or a package that
# fails changes nothing. Into the install's own folder, not the current one: the updater's is System32,
# where every update used to leave the package. A crashed run's leftovers go first.
New-Item -ItemType Directory -Path "$destinationPath/bin" -Force | Out-Null;
$stagingDir = "$destinationPath/bin/.staging";
Remove-Item -Path $stagingDir, "$destinationPath/bin/.package.zip" -Recurse -Force -ErrorAction Ignore;

# A version already in bin is complete and is kept, and nothing is downloaded for it. Another is
# unpacked into a staging folder that is renamed into bin whole, so a version in bin is never half there.
if (Test-Path $binDir) {
	Write-Output "Already installed: $binDir";
}
else {
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

	Write-Output "Extracting to $binDir";
	Expand-Archive "$packageFile" -DestinationPath $stagingDir -Force;
	if (-not (Test-Path "$stagingDir/$versionTag/$assemblyName.exe")) {
		throw "The package holds no $versionTag/$assemblyName.exe: $packageFile";
	}
	# the package keeps the build's times; the cleanup below goes by when a version was installed
	(Get-Item "$stagingDir/$versionTag").LastWriteTime = Get-Date;
	Move-Item -Path "$stagingDir/$versionTag" -Destination $binDir;
	Remove-Item -Path $stagingDir -Recurse -Force -ErrorAction Continue;
	if ("$downloadedPackageFile" -ne "") { Remove-Item -Path "$downloadedPackageFile" -Force -ErrorAction Continue; }
}
# the package the updater of earlier releases left in its working folder, System32
Remove-Item -Path "${env:SystemRoot}/System32/$assemblyName-win.zip" -Force -ErrorAction Ignore;

# Stopping the old server by its stop command, as systemd's stop does on Linux: a server that is
# ended keeps no chance to remove its NAT. The task is disabled first, or its repetition starts the old
# version again; one that does not stop is ended, and its files are free before they are replaced.
# The updater's task is never stopped, disabled or replaced here: an install that fails would leave
# nothing to bring it back.
Write-Output "Stopping $jobName (if any)...";
$isTaskEnabled = (Get-ScheduledTask -TaskName $jobName -ErrorAction Ignore).State -notin @($null, "Disabled");
$isServerStopped = $true;
Disable-ScheduledTask -TaskName $jobName -ErrorAction Ignore | Out-Null;
if (Test-Path "$destinationPath/$launcher.ps1") {
	# In a process of its own: this script comes through iex, and Windows 10 and 11 run no script file by
	# default, which left the server to be ended below. A native command's stderr is an error here.
	try {
		powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "$destinationPath/$launcher.ps1" stop | Out-Null;
		if ($LASTEXITCODE -ne 0) { throw "exit code $LASTEXITCODE"; }
	}
	catch { Write-Output "$jobName did not stop by its stop command: $_"; }
}
Start-Process "schtasks" "/end /tn $jobName" -Wait;
for ($i = 0; $i -lt 60; $i++) {
	$running = @(Get-Process -Name $assemblyName -ErrorAction Ignore | Where-Object { $_.Path -and $_.Path.StartsWith("$installFullPath\", [StringComparison]::OrdinalIgnoreCase) });
	if ($running.Count -eq 0) { break; }
	Start-Sleep -Milliseconds 500;
}

# From the stop on, a failure starts the server's task again on its way out, as the Linux installer's
# trap does. A trap holds for its whole scope; before the stop there is nothing to start.
trap {
	if ($isServerStopped -and $isTaskEnabled) {
		Write-Output "The install failed; starting $jobName again.";
		Enable-ScheduledTask -TaskName $jobName -ErrorAction Ignore | Out-Null;
		Start-Process "schtasks" "/run /tn $jobName";
	}
	break;
}

# Updating shared files...
Write-Output "Updating shared files...";
$infoDir = "$binDir/publish_info";
Copy-Item -path "$infoDir/vhupdate.ps1" -Destination "$destinationPath/" -Force;
Copy-Item -path "$infoDir/vhuninstall.ps1" -Destination "$destinationPath/" -Force;
Copy-Item -path "$infoDir/$launcher.ps1" -Destination "$destinationPath/" -Force;
Copy-Item -path "$infoDir/publish.json" -Destination "$destinationPath/" -Force;

# Windows' installed apps lists it, its Uninstall running vhuninstall.ps1; the version and the icon
# follow each install
$uninstallKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$assemblyName";
New-Item -Path $uninstallKey -Force | Out-Null;
$uninstallValues = @{
	DisplayName     = $productName;
	DisplayVersion  = $versionTag -replace '^v';
	Publisher       = "$(companyNameParam)";
	DisplayIcon     = "$installFullPath\bin\$versionTag\$assemblyName.exe";
	InstallLocation = $installFullPath;
	UninstallString = "powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File `"$installFullPath\vhuninstall.ps1`"";
};
foreach ($name in $uninstallValues.Keys) {
	Set-ItemProperty -Path $uninstallKey -Name $name -Value $uninstallValues[$name];
}
$estimatedSize = [int]((Get-ChildItem -Path $installFullPath -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1KB);
Set-ItemProperty -Path $uninstallKey -Name "EstimatedSize" -Value $estimatedSize -Type DWord;
Set-ItemProperty -Path $uninstallKey -Name "NoModify" -Value 1 -Type DWord;
Set-ItemProperty -Path $uninstallKey -Name "NoRepair" -Value 1 -Type DWord;

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

# The minimum client protocol settled above, through a new file
if ($minClientProtocolWrite -ne "") {
	$minClientProtocolSettings = switch ($minClientProtocolWrite) {
		"replace" { ([regex]'("MinClientProtocolVersion"\s*:\s*)\d+').Replace($settingsText, "`${1}$minClientProtocolVersion", 1) }
		"insert" { ([regex]'\{').Replace($settingsText, "{`n  `"ServerConfig`": { `"Session`": { `"MinClientProtocolVersion`": $minClientProtocolVersion } },", 1) }
		default { "{`n  `"FileAccessManager`": {`n    `"Session`": {`n      `"MinClientProtocolVersion`": $minClientProtocolVersion`n    }`n  }`n}`n" }
	};
	New-Item -ItemType Directory -Force -Path "$destinationPath/storage" | Out-Null;
	[IO.File]::WriteAllText("$settingsFile.new", $minClientProtocolSettings);
	Move-Item -Path "$settingsFile.new" -Destination $settingsFile -Force;
	Write-Output "Minimum client protocol version: $minClientProtocolVersion";
}

# The server's task, which starts it at boot and keeps it running; stopped above, it stays disabled when
# its choice is no
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Seconds 0);
if ($autostart -eq "y") {
	Write-Output "creating autostart service... Name: $jobName";
	$action = New-ScheduledTaskAction -Execute "$binDir/$assemblyName.exe";
	$trigger1 = New-ScheduledTaskTrigger -AtStartup;
	$trigger2 = New-ScheduledTaskTrigger -once -RepetitionInterval "00:01:00" -At (Get-Date);
	$task = New-ScheduledTask -Action $action -Trigger @($trigger1, $trigger2) -Settings $settings;
	$registerJob = Register-ScheduledTask -User "System" -TaskName "$jobName" -InputObject $task -Force -AsJob;
}

# The updater's task, its own choice apart from the boot's. Bypass: a client edition's default
# execution policy runs no script, and the task then failed before its first line on every run.
# Registered only when missing or changed, and never ended: the updater's run is often the one
# installing, and an install that stopped it would leave nothing to bring it back.
$updaterTask = Get-ScheduledTask -TaskName "${jobName}Updater" -ErrorAction Ignore;
if ($autoupdate -eq "y") {
	$updaterArguments = "-NonInteractive -NoLogo -NoProfile -ExecutionPolicy Bypass -File `"$destinationPath/vhupdate.ps1`" -q";
	if (-not $updaterTask -or $updaterTask.Actions[0].Arguments -ne $updaterArguments) {
		Write-Output "creating auto update service... Name: ${jobName}Updater";
		$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $updaterArguments;
		$trigger = New-ScheduledTaskTrigger -Daily -At 3am;
		$task = New-ScheduledTask -Action $action -Trigger $trigger -Settings $settings;
		Register-ScheduledTask -User "System" -TaskName "${jobName}Updater" -InputObject $task -Force | Out-Null;
	}
	elseif ($updaterTask.State -eq "Disabled") {
		Enable-ScheduledTask -TaskName "${jobName}Updater" | Out-Null;
	}
}
elseif ($updaterTask -and $updaterTask.State -ne "Disabled") {
	Write-Output "Turning off the auto update service... Name: ${jobName}Updater";
	Disable-ScheduledTask -TaskName "${jobName}Updater" | Out-Null;
}

if ($autostart -eq "y") {
	# run once the task is the new version's, and enabled again
	Wait-Job $registerJob -Timeout 60 | Out-Null;
	Start-Process "schtasks" "/run /tn $jobName";
}

Write-Output "$productName has been installed.";
Write-Output "To uninstall it: Installed apps, or $installFullPath\vhuninstall.ps1";

# The firewall is the administrator's to open, as on Linux: this says only that it is on, as the ports
# are the server's settings, which can change after this
if (@(Get-NetFirewallProfile -ErrorAction Ignore | Where-Object { "$($_.Enabled)" -eq "True" }).Count -gt 0) {
	Write-Output "NOTE: Windows Firewall is on. Clients reach the server only through ports it allows:";
	Write-Output "      allow the ports the server listens on (TCP 443 by default).";
}
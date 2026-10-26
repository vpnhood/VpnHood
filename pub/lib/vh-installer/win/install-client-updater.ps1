# The client install's updater, vhupdate.ps1 in the install's root (install-client.nsi), Linux's vhupdate on Windows.
# Outside the service, so that a release whose service is broken still gets the next one: run by its scheduled task
# at boot and every 12 hours, and by the service with -packageFile - the notice's Install, or a download handed back
# after a disconnect. It downloads a newer release at any time - by BITS, Windows' own downloader, which goes on
# between runs and resumes a cut download - into the install's updates folder, where the app finds it too, and runs
# its setup silently only while the VPN is idle - or when the service does not answer, as a broken one would not;
# else the download waits there for the service. The setup stops the service and closes what runs from the install,
# then starts the new version, which opens again the windows and trays it closed. Its output goes to update.log beside
# it, whose last line says why when nothing was installed. A setup given describes itself, by its own version, so
# nothing is read from the server - which a filtered network may let through only in the tunnel, down again after a
# disconnect.
param([string]$packageFile = "")
$ErrorActionPreference = "Stop"
# the web cmdlets' progress bar slows them many times over
$ProgressPreference = "SilentlyContinue"
$root = $PSScriptRoot
$updatesPath = Join-Path $root "updates"
$logPath = Join-Path $root "update.log"
$publishInfoPath = Join-Path $root "current\publish.json"

# Write-Host, not Write-Output, which would make each line part of the function's answer
function Write-Log([string]$message) {
	Write-Host $message
	Add-Content -Path $logPath -Value $message -Encoding UTF8
}

# The task's runs, not the service's -packageFile ones: a run that cannot reach the feed or the package leaves
# update.missed beside this script, for the service to run the task again once its VPN connects - through the tunnel,
# where a filtered network may let it through - and one that reaches them takes it away.
function Set-Missed([bool]$isMissed) {
	if ($packageFile) { return }
	$missedPath = Join-Path $root "update.missed"
	if ($isMissed) { $null = New-Item $missedPath -ItemType File -Force }
	else { Remove-Item $missedPath -Force -ErrorAction SilentlyContinue }
}

# The files in the updates folder but the one given: a release overtaken, one installed - one updater runs at a time
# - and the BITS jobs of the others, which take their parts along. Never the app's download (.part), its own to keep;
# the part BITS writes for the one given is hidden, so passed by.
function Remove-Downloads([string]$keepPath) {
	Get-ChildItem $updatesPath -File -ErrorAction SilentlyContinue |
		Where-Object { $_.FullName -ne $keepPath -and $_.Extension -ne ".part" } |
		ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }
	$keepJobName = Get-JobName $keepPath
	Get-BitsTransfer -ErrorAction SilentlyContinue |
		Where-Object { $_.DisplayName -like "$instanceName update *" -and $_.DisplayName -ne $keepJobName } |
		Remove-BitsTransfer -ErrorAction SilentlyContinue
}

# the BITS job of a release's download, by its package's name; the uninstaller cancels them by this name too
function Get-JobName([string]$setupPath) {
	"$instanceName update $([IO.Path]::GetFileName($setupPath))"
}

# The release's download by BITS, as Windows Update's: a job of SYSTEM's per release, which goes on by itself between
# runs - across network drops, sleeps and restarts - and resumes where it stopped. This run waits while it moves; one
# that stops moving for a minute is left to BITS, for a later run to find done or going on. A job that failed for good
# goes, and the next run starts it again.
function Receive-Package([Uri]$packageUrl, [string]$setupPath) {
	$jobName = Get-JobName $setupPath
	$job = @(Get-BitsTransfer | Where-Object DisplayName -eq $jobName)[0]
	if (-not $job) {
		$null = New-Item $updatesPath -ItemType Directory -Force
		$job = Start-BitsTransfer -Source $packageUrl -Destination $setupPath -DisplayName $jobName -Asynchronous `
			-Priority Foreground -RetryInterval 60
	}
	elseif ($job.JobState -in "Suspended", "Error", "TransientError") {
		# at once, rather than at BITS's next retry: this run may be the service's, the VPN just connected
		$null = Resume-BitsTransfer -BitsJob $job -Asynchronous
	}

	$bytes = $job.BytesTransferred
	$movedAt = Get-Date
	while ($job.JobState -notin "Transferred", "Error" -and ((Get-Date) - $movedAt).TotalSeconds -lt 60) {
		Start-Sleep -Seconds 1
		if ($job.BytesTransferred -ne $bytes) {
			$bytes = $job.BytesTransferred
			$movedAt = Get-Date
		}
	}

	if ($job.JobState -eq "Transferred") {
		Complete-BitsTransfer -BitsJob $job
		return $true
	}

	if ($job.JobState -eq "Error") {
		Write-Log "BITS could not download it: $("$($job.ErrorDescription)".Trim())"
		Remove-BitsTransfer -BitsJob $job
	}
	else {
		Write-Log "BITS goes on with the download, $([Math]::Round($bytes / 1MB, 1)) MB so far ($($job.JobState))."
	}
	return $false
}

function Update-App {
	$localInfo = Get-Content $publishInfoPath -Raw | ConvertFrom-Json
	# GitHub takes TLS 1.2 or later, which Windows PowerShell does not offer by default everywhere
	[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
	$version = $null
	if ($packageFile) {
		# the setup's own ProductVersion (install-client.nsi); one without it goes, as a cut download would
		if (-not [Version]::TryParse((Get-Item $packageFile).VersionInfo.ProductVersion, [ref]$version)) {
			Write-Log "Could not read the package! File: $packageFile"
			if ($packageFile.StartsWith("$updatesPath\", [StringComparison]::OrdinalIgnoreCase)) {
				Remove-Item $packageFile -Force -ErrorAction SilentlyContinue
			}
			return 1
		}
	}
	else {
		try {
			$onlineInfo = Invoke-RestMethod -Uri $localInfo.UpdateInfoUrl -TimeoutSec 60 -UseBasicParsing
		}
		catch {
			Write-Log "Could not retrieve the latest package information! Url: $($localInfo.UpdateInfoUrl)"
			Set-Missed $true
			return 1
		}
		Set-Missed $false
		$version = [Version]$onlineInfo.Version
	}

	Write-Log "Installed version: $($localInfo.Version)"
	Write-Log "$(if ($packageFile) { 'Package' } else { 'Latest' }) version: $version"
	if ([Version]$localInfo.Version -ge $version) {
		Write-Log "The installed version is up to date."
		if (-not $packageFile) { Remove-Downloads "" }
		return 0
	}

	$setupPath = $packageFile
	if (-not $setupPath) {
		# https alone, as the app's own download takes it, or a test feed on this machine
		$packageUrl = [Uri]$onlineInfo.PackageUrl
		if ($packageUrl.Scheme -ne "https" -and -not $packageUrl.IsLoopback) {
			Write-Log "The package is not on https: $packageUrl"
			return 1
		}

		# downloaded under the app's name for it (DesktopAppUpdaterProvider), which BITS gives the file once it is whole
		$setupPath = Join-Path $updatesPath "$($onlineInfo.Version)-$([IO.Path]::GetFileName($packageUrl.AbsolutePath))"
		Remove-Downloads $setupPath
		if (-not (Test-Path $setupPath)) {
			Write-Log "Downloading $($onlineInfo.Version)..."
			if (-not (Receive-Package $packageUrl $setupPath)) {
				Write-Log "Could not download the update! Url: $packageUrl"
				Set-Missed $true
				return 1
			}
		}

		# what the service says of its VPN: 0 and "connected", 3 and its state, any other code no answer. A
		# release built without the console launcher cannot be asked.
		if ($localInfo.CommandFile) {
			# Continue: under Stop, Windows PowerShell throws at a native command's first line on stderr, which the
			# command writes when the service does not answer
			$ErrorActionPreference = "Continue"
			$stateJson = & (Join-Path $root $localInfo.CommandFile) status --json 2>$null | Out-String
			$stateCode = $LASTEXITCODE
			$ErrorActionPreference = "Stop"
			$connectionState = try { ($stateJson | ConvertFrom-Json).connectionState } catch { $null }
			if ($stateCode -eq 0 -or ($stateCode -eq 3 -and $connectionState -ne "None")) {
				Write-Log "The VPN is in use ($connectionState): $($onlineInfo.Version) is installed after it disconnects."
				return 0
			}
		}
	}

	# Here again only when the setup ended before it stopped the service, or this run was not the service's: a
	# setup that installs stops the service that ran this, and the new version starts. A setup that fails its own
	# check, spliced or cut short, goes, and the next run downloads it again whole.
	Write-Log "Installing $version..."
	$setup = Start-Process -FilePath $setupPath -ArgumentList "/S" -Wait -PassThru
	if ($setup.ExitCode -ne 0) {
		if ($setupPath.StartsWith("$updatesPath\", [StringComparison]::OrdinalIgnoreCase)) {
			Remove-Item $setupPath -Force -ErrorAction SilentlyContinue
		}
		Write-Log "The setup ended with code $($setup.ExitCode)."
		return 1
	}

	Write-Log "Installed $version."
	return 0
}

# One updater at a time - the task's, the service's, the notice's - each reading the install only once the one before
# has ended: a run that waited finds the update done, at the version check. A run that ended without letting go
# leaves the mutex abandoned, which is as good as free. The setup keeps a mutex of its own, for one run by hand.
$instanceName = [IO.Path]::GetFileNameWithoutExtension((Get-Content $publishInfoPath -Raw | ConvertFrom-Json).ExeFile)
$mutex = [Threading.Mutex]::new($false, "Global\$instanceName-update")
try {
	try { $null = $mutex.WaitOne() } catch [Threading.AbandonedMutexException] { }
	Set-Content -Path $logPath -Value "Updating $instanceName..." -Encoding UTF8
	$exitCode = Update-App
}
catch {
	Write-Log "The update failed: $($_.Exception.Message)"
	$exitCode = 1
}
finally {
	$mutex.ReleaseMutex()
	$mutex.Dispose()
}
exit $exitCode

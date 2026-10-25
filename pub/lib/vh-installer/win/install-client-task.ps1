param(
	[Parameter(Mandatory = $true)] [string]$taskName,
	[Parameter(Mandatory = $true)] [string]$scriptPath,
	# "on" or "off" as the setup was told (/AUTOUPDATE, /NOAUTOUPDATE); none keeps an earlier install's choice, and a
	# new install updates itself
	[string]$choice = ""
)
# The client install's updater task (install-client.nsi): vhupdate.ps1 as SYSTEM at boot and every 12 hours, the
# way Linux's timer runs vhupdate. Through schtasks, which any Windows PowerShell can run, the setup's 32-bit one
# included; the task's own XML, which names every setting rather than leaving one to a cmdlet's default.
$ErrorActionPreference = "Stop"

$enabled = $choice -ne "off"
if ($choice -eq "") {
	# Continue: under Stop, Windows PowerShell throws at a native command's first line on stderr, redirected or not
	$ErrorActionPreference = "Continue"
	$existing = & schtasks.exe /Query /TN $taskName /XML 2>$null
	$ErrorActionPreference = "Stop"
	if ($LASTEXITCODE -eq 0) {
		$enabled = ([xml]($existing -join "`n")).Task.Settings.Enabled -ne "false"
	}
}

# the boot's run a few minutes on, when the network is up; StartWhenAvailable: a run missed while the machine was
# off or asleep runs once it is back
$powerShell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
$arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$scriptPath`""
$xml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Installs a newer release of $([Security.SecurityElement]::Escape((Split-Path $scriptPath -Parent))) while its VPN is idle.</Description>
  </RegistrationInfo>
  <Triggers>
    <BootTrigger>
      <Delay>PT5M</Delay>
    </BootTrigger>
    <TimeTrigger>
      <StartBoundary>$((Get-Date).ToString("s"))</StartBoundary>
      <Repetition>
        <Interval>PT12H</Interval>
      </Repetition>
      <RandomDelay>PT10M</RandomDelay>
    </TimeTrigger>
  </Triggers>
  <Principals>
    <Principal id="System">
      <UserId>S-1-5-18</UserId>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <StartWhenAvailable>true</StartWhenAvailable>
    <ExecutionTimeLimit>PT2H</ExecutionTimeLimit>
    <Enabled>$($enabled.ToString().ToLowerInvariant())</Enabled>
  </Settings>
  <Actions Context="System">
    <Exec>
      <Command>$([Security.SecurityElement]::Escape($powerShell))</Command>
      <Arguments>$([Security.SecurityElement]::Escape($arguments))</Arguments>
    </Exec>
  </Actions>
</Task>
"@

$xmlPath = Join-Path ([IO.Path]::GetTempPath()) "$taskName.xml"
Set-Content -Path $xmlPath -Value $xml -Encoding Unicode
try {
	& schtasks.exe /Create /TN $taskName /XML $xmlPath /F | Out-Null
	if ($LASTEXITCODE -ne 0) { throw "schtasks could not register $taskName (exit $LASTEXITCODE)." }
}
finally {
	Remove-Item $xmlPath -Force -ErrorAction SilentlyContinue
}
Write-Output "$taskName registered, $(if ($enabled) { 'on' } else { 'off' })."

param(
	[String]$versionFile,
	# 0 = read only (no mutation), 1 = stable release bump, 2 = prerelease bump,
	# 3 = mark the CURRENT version as a prerelease WITHOUT incrementing it.
	[int]$bump
)

# Mode 3 exists because "which channel does this version ship on" and "mint a new build number" are
# separate decisions. Shipping an app from an already-bumped version on the prerelease channel (e.g.
# Connect going to Play alpha at a version the client already built) otherwise forced a hand-edit of
# PubVersion.json, which is exactly what this file is meant to be the only writer of.
$markPrereleaseOnly = ($bump -eq 3);

$versionJson = (Get-Content $versionFile | Out-String | ConvertFrom-Json);
$bumpTime = [datetime]::Parse($versionJson.BumpTime);
$version = [version]::Parse($versionJson.Version);
if ( $bump -gt 0 )
{
	$isVersionBumped = $true;
	if ( -not $markPrereleaseOnly )
	{
		$version = [version]::new($version.Major, $version.Minor, $version.Build + 1);
		$versionJson.Version = $version.ToString(3);
		$versionJson.BumpTime = [datetime]::UtcNow.ToString("o");
	}
	$versionJson.Prerelease = ($bump -eq 2 -or $markPrereleaseOnly);
	$versionJson | ConvertTo-Json -depth 10 | Out-File $versionFile;

	# Mirror the version into the root Directory.Build.props — the single <Version> every project (apps +
	# libraries) inherits, and the only place the version is stamped. CI can still override a pack with -p:Version.
	# Skipped when only the channel changed: the number in the props file is already correct.
	$srcPropsFile = Join-Path (Split-Path -Parent (Split-Path -Parent $versionFile)) "Directory.Build.props";
	if ( (-not $markPrereleaseOnly) -and (Test-Path $srcPropsFile) ) {
		$props = Get-Content $srcPropsFile -Raw;
		$props = ([regex]"<Version>.*?</Version>").Replace($props, "<Version>$($versionJson.Version)</Version>", 1);
		Set-Content -Path $srcPropsFile -Value $props -Encoding utf8 -NoNewline;
	}
}

$prerelease = $versionJson.Prerelease;
$versionCode = $version.Build;
$isLatest = $versionJson.Prerelease -eq $false; 
$versionParam = $version.ToString(3);
$versionTag = "v$versionParam" + (&{if($prerelease) {"-prerelease"} else {""}});
$releaseDate = (Get-Date).ToUniversalTime().ToString("s");
$releaseFlag = if ($prerelease) { "--prerelease" } else { "--latest" };
$deprecatedVersion = $versionJson.DeprecatedVersion;
$versionNotificationDelay = $versionJson.NotificationDelay;

if ( $markPrereleaseOnly )
{
	Write-Host "Version $versionParam marked as a PRERELEASE (not incremented)" -ForegroundColor Blue;
}
elseif ( $bump -gt 0 )
{
	Write-Host "Version has been bumped to: $versionParam" -ForegroundColor Blue;
}

# ReportVersion
Function ReportVersion() 
{
	Write-Host "version: $versionParam" -ForegroundColor GREEN;
}

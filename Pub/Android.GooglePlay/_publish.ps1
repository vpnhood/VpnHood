. "$PSScriptRoot/../Common.ps1"

Write-Host "";
Write-Host "*** Publishing Android of GooglePlay  ..." -BackgroundColor Blue -ForegroundColor White;

$projectDir = $PSScriptRoot

# prepare module folders
$moduleDir = "$projectDir/latest";
$module_infoFile = "$moduleDir/VpnHoodClient-android.json";
$module_packageFile = "$moduleDir/VpnHoodClient-android.apk";

# Calcualted Path
$module_infoFileName = $(Split-Path "$module_infoFile" -leaf);
$module_packageFileName = $(Split-Path "$module_packageFile" -leaf);


#find the apk in current folder
$apkFileData = Get-ChildItem -Path $projectDir -Filter *.apk | Select-Object -First 1;
if ($apkFileData -eq $null)
{
	Write-Host "No apk file found in $projectDir" -ForegroundColor Red;
	exit;
}
$apkFile = $apkFileData.FullName;
$apkVersionCode = (Get-Item $apkFile).Basename;
$version = [version]::new($version.Major, $version.Minor, $apkVersionCode);
$versionParam = $version.ToString(3);
$versionTag="v$versionParam";

# publish info
$json = @{
    Version = $versionParam; 
    UpdateInfoUrl = "https://github.com/vpnhood/VpnHood/releases/latest/download/$module_infoFileName";
    PackageUrl = "https://github.com/vpnhood/VpnHood/releases/download/$versionTag/$module_packageFileName";
	InstallationPageUrl = "https://github.com/vpnhood/VpnHood/wiki/Install-VpnHood-Client";
	ReleaseDate = "$releaseDate";
	DeprecatedVersion = "$deprecatedVersion";
	NotificationDelay = "14.00:00:00";
};
$json | ConvertTo-Json | Out-File "$module_infoFile" -Encoding ASCII;

# move the apk
Move-Item -Path $apkFile -Destination $module_packageFile -Force;

# apk
Write-Host;
Write-Host "*** Updating Android apk of GooglePlay to $versionTag ..." -BackgroundColor Blue -ForegroundColor White;

# $latestVersion = (gh release list -R "vpnhood/vpnhood" --limit 1 --exclude-drafts  --exclude-pre-releases | ForEach-Object { $_.Split()[0] });
gh release upload $versionTag $module_infoFile $module_packageFile --clobber


. "$PSScriptRoot/../Core/Common.ps1"

Write-Host "";
Write-Host "*** Publishing Android of GooglePlay  ..." -BackgroundColor Blue -ForegroundColor White;

$projectDir = $PSScriptRoot


#find the apk in current folder
$apkFileData = Get-ChildItem -Path $projectDir -Filter *.apk | Select-Object -First 1;
if ($apkFileData -eq $null)
{
	Write-Host "No apk file found in $projectDir" -ForegroundColor Red;
	exit;
}
$apkFile = $apkFileData.FullName;
$apkVersionCode = (Get-Item $apkFile).Basename;
if ($apkVersionCode -ne $version.Build)
{
	throw "The apk version code $apkVersionCode is not equal to the build version $($version.Build)";
}

# prepare module folders
$moduleDir = "$projectDir/apk/$versionTag";
$moduleDirLatest = "$projectDir/apk/latest";
$module_infoFile = "$moduleDir/VpnHoodClient-android.json";
$module_packageFile = "$moduleDir/VpnHoodClient-android.apk";
PrepareModuleFolder $moduleDir $moduleDirLatest;

# Calcualted Path
$module_infoFileName = $(Split-Path "$module_infoFile" -leaf);
$module_packageFileName = $(Split-Path "$module_packageFile" -leaf);

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
Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse;

# Publishing to GitHub
Push-Location -Path "$solutionDir";

# apk
# Write-Host;
# Write-Host "*** Updating Android apk of GooglePlay to $versionTag ..." -BackgroundColor Blue -ForegroundColor White;
# $latestVersion = (gh release list -R "vpnhood/vpnhood" --limit 1 --exclude-drafts  --exclude-pre-releases | ForEach-Object { $_.Split()[0] });

echo "Updating the Release ...";
gh release upload $versionTag $module_infoFile $module_packageFile --clobber;

echo "Updating the Pre-release ...";
gh release upload "$versionTag-prerelease" $module_infoFile $module_packageFile --clobber;

Pop-Location
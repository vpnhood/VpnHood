. "$PSScriptRoot/../Pub/Common.ps1"

Write-Host "";
Write-Host "*** Publishing Android ..." -BackgroundColor Blue -ForegroundColor White;

$projectDir = $PSScriptRoot
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;

# prepare module folders
$moduleDir = "$packagesClientDir/android";
$moduleDirLatest = "$packagesClientDirLatest/android";
PrepareModuleFolder $moduleDir $moduleDirLatest;

$module_infoFile = "$moduleDir/VpnHoodClient-android.json";
$module_packageFile = "$moduleDir/VpnHoodClient-android.apk";

# Calcualted Path
$module_infoFileName = $(Split-Path "$module_infoFile" -leaf);
$module_packageFileName = $(Split-Path "$module_packageFile" -leaf);

# android
$keystore = Join-Path "$solutionDir/../.user/" $credentials.Android.KeyStoreFile
$keystorePass = $credentials.Android.KeyStorePass
$keystoreAlias = $credentials.Android.KeyStoreAlias
$manifestFile = Join-Path $projectDir "Properties/AndroidManifest.xml";
$manifestFileAab = Join-Path $projectDir "Properties/AndroidManifest.aab.xml";

# set android version
$xmlDoc = [xml](Get-Content $manifestFile)
$xmlDoc.manifest.versionCode = $version.Build.ToString()
$xmlDoc.manifest.versionName = $version.ToString(3)
$xmlDoc.save($manifestFile);

# apk
Write-Host;
Write-Host "*** Creating Android APK ..." -BackgroundColor Blue -ForegroundColor White;

$packageId = "com.vpnhood.client.droid";
$signedApk = Join-Path $projectDir "bin/releaseApk/$packageId-Signed.apk"
if (-not $noclean)  { & $msbuild $projectFile /p:Configuration=Release /t:Clean /p:OutputPath="bin/ReleaseApk" /verbosity:$msverbosity; }
 & $msbuild $projectFile /p:Configuration=Release /t:SignAndroidPackage  /p:Version=$versionParam /p:OutputPath="bin/ReleaseApk" /p:AndroidPackageFormat="apk" /verbosity:$msverbosity `
	/p:AndroidSigningKeyStore=$keystore /p:AndroidSigningKeyAlias=$keystoreAlias /p:AndroidSigningStorePass=$keystorePass `
	/p:JarsignerTimestampAuthorityUrl="https://freetsa.org/tsr";

# aab
Write-Host;
Write-Host "*** Creating Android AAB ..." -BackgroundColor Blue -ForegroundColor White;

$packageId = "com.vpnhood.client.android";
$xmlDoc.manifest.package = $packageId;
$xmlDoc.save($manifestFileAab);

if (-not $noclean)  { & $msbuild $projectFile /p:Configuration=Release /t:Clean /verbosity:$msverbosity; }
& $msbuild $projectFile /p:Configuration=Release /p:Version=$versionParam /t:SignAndroidPackage /p:ArchiveOnBuild=true /verbosity:$msverbosity `
	/p:AndroidSigningKeyStore=$keystore /p:AndroidSigningKeyAlias=$keystoreAlias /p:AndroidSigningStorePass=$keystorePass `
	/p:AndroidManifest="Properties\AndroidManifest.aab.xml" /p:DefineConstants=ANDROID_AAB `
	/p:AndroidSigningKeyPass=$keystorePass /p:AndroidKeyStore=True;

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

#####
# copy to solution ouput
Copy-Item -path $signedApk -Destination "$moduleDir/$module_packageFileName" -Force
if ($isLatest)
{
	Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse
}

# report version
ReportVersion

. "$PSScriptRoot/../Pub/Common.ps1"

Write-Host "";
Write-Host "*** Creating Android Bundle AAB ..." -BackgroundColor Blue -ForegroundColor White;

$projectDir = $PSScriptRoot
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;
$packageFileName = "VpnHoodClient-Android.apk";

# prepare module folders
$moduleDir = "$packagesClientDir/android";
$moduleDirLatest = "$packagesClientDirLatest/android";
PrepareModuleFolder $moduleDir $moduleDirLatest;

# android
$keystore = Join-Path "$solutionDir/../.user/" $credentials.Android.KeyStoreFile
$keystorePass = $credentials.Android.KeyStorePass
$keystoreAlias = $credentials.Android.KeyStoreAlias
$manifestFile = Join-Path $projectDir "Properties/AndroidManifest.xml";

# set android version
$xmlDoc = [xml](Get-Content $manifestFile)
$xmlDoc.manifest.versionCode = $version.Build.ToString()
$xmlDoc.manifest.versionName = $version.ToString(3)
$xmlDoc.save($manifestFile);

$packageId = $xmlDoc.manifest.package;
$signedApk= Join-Path $projectDir "bin/releaseApk/$packageId-Signed.apk"

# bundle (aab)
if (-not $noclean)  { & $msbuild $projectFile /p:Configuration=Release /t:Clean /verbosity:$msverbosity; }
& $msbuild $projectFile /p:Configuration=Release /p:Version=$versionParam /t:SignAndroidPackage /p:ArchiveOnBuild=true /verbosity:$msverbosity `
	/p:AndroidKeyStore=True /p:AndroidSigningKeyStore=$keystore /p:AndroidSigningKeyAlias=$keystoreAlias /p:AndroidSigningKeyPass=$keystorePass /p:AndroidSigningStorePass=$keystorePass 

# apk
Write-Host;
Write-Host "*** Creating Android APK ..." -BackgroundColor Blue -ForegroundColor White;

if (-not $noclean)  { & $msbuild $projectFile /p:Configuration=Release /t:Clean /p:OutputPath="bin/ReleaseApk" /verbosity:$msverbosity; }
& $msbuild $projectFile /p:Configuration=Release /t:SignAndroidPackage  /p:Version=$versionParam /p:OutputPath="bin/ReleaseApk" /p:AndroidPackageFormat="apk" /verbosity:$msverbosity `
	/p:AndroidSigningKeyStore=$keystore /p:AndroidSigningKeyAlias=$keystoreAlias /p:AndroidSigningStorePass=$keystorePass /p:JarsignerTimestampAuthorityUrl="https://freetsa.org/tsr"

#####
# copy to solution ouput
Copy-Item -path $signedApk -Destination "$moduleDir/$packageFileName" -Force
if ($isLatest)
{
	Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse
}

# report version
ReportVersion

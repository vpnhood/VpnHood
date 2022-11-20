. "$PSScriptRoot/../Pub/Common.ps1"

$projectDir = $PSScriptRoot
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;
$packageFileName = "VpnHoodClient-Android.apk";
$moduleDir = "$packagesClientDir/android";
$moduleDirLatest = "$packagesClientDirLatest/android";

# prepare module folders
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
if (-not $noclean)  { & $msbuild $projectFile /p:Configuration=Release /t:Clean; }
& $msbuild $projectFile /p:Configuration=Release /p:Version=$versionParam /t:SignAndroidPackage /p:ArchiveOnBuild=true `
	/p:AndroidKeyStore=True /p:AndroidSigningKeyStore=$keystore /p:AndroidSigningKeyAlias=$keystoreAlias /p:AndroidSigningKeyPass=$keystorePass /p:AndroidSigningStorePass=$keystorePass 

# apk
if (-not $noclean)  { & $msbuild $projectFile /p:Configuration=Release /t:Clean /p:OutputPath="bin/ReleaseApk"; }
& $msbuild $projectFile /p:Configuration=Release /t:SignAndroidPackage  /p:Version=$versionParam /p:OutputPath="bin/ReleaseApk" /p:AndroidPackageFormat="apk" `
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

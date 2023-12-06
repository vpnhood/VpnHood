param(
	[Parameter(Mandatory=$true)] [String]$projectDir, 
	[Parameter(Mandatory=$true)] [String]$packageFileTitle)

. "$PSScriptRoot/Common.ps1"

$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;

Write-Host "";
Write-Host "*** Publishing $projectFile ..." -BackgroundColor Blue -ForegroundColor White;

#update project version
UpdateProjectVersion $projectFile;

# prepare module folders
$moduleDir = "$packagesRootDir/$packageFileTitle/android";
$moduleDirLatest = "$packagesRootDirLatest/$packageFileTitle/android";
PrepareModuleFolder $moduleDir $moduleDirLatest;

$module_infoFile = "$moduleDir/$packageFileTitle-android-web.json";
$module_packageFile = "$moduleDir/$packageFileTitle-android-web.apk";

# Calcualted Path
$module_infoFileName = $(Split-Path "$module_infoFile" -leaf);
$module_packageFileName = $(Split-Path "$module_packageFile" -leaf);

# android
$keystore = Join-Path "$solutionDir/../.user/" $credentials.Android.KeyStoreFile
$keystorePass = $credentials.Android.KeyStorePass
$keystoreAlias = $credentials.Android.KeyStoreAlias
$manifestFile = Join-Path $projectDir "Properties/AndroidManifest.xml";
$appIconXml = Join-Path $projectDir "Resources\mipmap-anydpi-v26\appicon.xml";
$appIconXmlDoc = [xml](Get-Content $appIconXml);
$appIconXmlNode = $appIconXmlDoc.selectSingleNode("adaptive-icon/background");
$packageId = ([xml](Get-Content $projectFile)).SelectSingleNode("Project/PropertyGroup/ApplicationId").InnerText;

# set app icon
$appIconXmlNode.SetAttribute("android:drawable", "@mipmap/appicon_background_dev");
$appIconXmlDoc.save($appIconXml);

# apk
Write-Host;
Write-Host "*** Creating Android APK ..." -BackgroundColor Blue -ForegroundColor White;

$outputPath = Join-Path $projectDir "bin/ReleaseApk/";
$signedPacakgeFile = Join-Path $outputPath "$packageId-Signed.apk"

# todo
if (-not $noclean)  { & $msbuild $projectFile /p:Configuration=Release /t:Clean /p:OutputPath=$outputPath /verbosity:$msverbosity; }
 & $msbuild $projectFile /p:Configuration=Release /t:SignAndroidPackage /p:Version=$versionParam /p:OutputPath=$outputPath /p:AndroidPackageFormat="apk" /verbosity:$msverbosity `
	/p:AndroidSigningKeyStore=$keystore /p:AndroidSigningKeyAlias=$keystoreAlias /p:AndroidSigningStorePass=$keystorePass `
	/p:ApplicationId=$packageId `
	/p:JarsignerTimestampAuthorityUrl="https://freetsa.org/tsr";

# publish info
$json = @{
    Version = $versionParam; 
    UpdateInfoUrl = "https://github.com/vpnhood/VpnHood/releases/latest/download/$module_infoFileName";
    PackageUrl = "https://github.com/vpnhood/VpnHood/releases/download/$versionTag/$module_packageFileName";
	InstallationPageUrl = "https://github.com/vpnhood/VpnHood/releases/download/$versionTag/$module_packageFileName";
	ReleaseDate = "$releaseDate";
	DeprecatedVersion = "$deprecatedVersion";
	NotificationDelay = "03.00:00:00";
};
$json | ConvertTo-Json | Out-File "$module_infoFile" -Encoding ASCII;

Copy-Item -path $signedPacakgeFile -Destination "$moduleDir/$module_packageFileName" -Force
if ($isLatest)
{
	Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse;
}

# ------------- aab
Write-Host;
Write-Host "*** Creating Android AAB ..." -BackgroundColor Blue -ForegroundColor White;

# set app icon
$appIconXmlNode.SetAttribute("android:drawable", "@mipmap/appicon_background");
$appIconXmlDoc.save($appIconXml);

# update variables
$packageId = $packageId.replace(".web", "");
$outputPath = Join-Path $projectDir "bin/ReleaseAab/";
$signedPacakgeFile = Join-Path "$outputPath" "$packageId-Signed.aab"
$module_packageFile = "$moduleDir/$packageFileTitle-android.aab";
$module_packageFileName = $(Split-Path "$module_packageFile" -leaf);

if (-not $noclean)  { & $msbuild $projectFile /p:Configuration=Release /t:Clean /p:OutputPath=$outputPath /verbosity:$msverbosity; }
& $msbuild $projectFile /p:Configuration=Release /p:Version=$versionParam /p:OutputPath=$outputPath /t:SignAndroidPackage /p:ArchiveOnBuild=true /verbosity:$msverbosity `
	/p:AndroidSigningKeyStore=$keystore /p:AndroidSigningKeyAlias=$keystoreAlias /p:AndroidSigningStorePass=$keystorePass `
	/p:ApplicationId=$packageId `
	/p:DefineConstants=ANDROID_AAB `
	/p:AndroidSigningKeyPass=$keystorePass /p:AndroidKeyStore=True;

# set app icon
$appIconXmlNode.SetAttribute("android:drawable", "@mipmap/appicon_background_dev");
$appIconXmlDoc.save($appIconXml);

#####
# copy to solution ouput
Copy-Item -path $signedPacakgeFile -Destination "$moduleDir/$module_packageFileName" -Force
if ($isLatest)
{
	Copy-Item -path $signedPacakgeFile -Destination "$moduleDirLatest/$module_packageFileName" -Force -Recurse;
	Copy-Item -path "$moduleGooglePlayLastestDir/*" -Destination "$moduleDirLatest/" -Force -Recurse;
}

# report version
ReportVersion
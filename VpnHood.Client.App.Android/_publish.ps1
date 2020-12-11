. "$PSScriptRoot\..\Pub\Common.ps1"

$projectDir = $PSScriptRoot
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;

# android
$keystore = Join-Path "$solutionDir\..\.user\" $credentials.Android.KeyStoreFile
$keystorePass = $credentials.Android.KeyStorePass
$keystoreAlias = $credentials.Android.KeyStoreAlias
$apkPublishDir = Join-Path $projectDir "bin\release\publish";
$signedApk= Join-Path $apkPublishDir "com.vpnhood.client.android-signed.apk"
$manifestFile = Join-Path $projectDir "Properties\AndroidManifest.xml";

# set android version
$xmlDoc = [xml](Get-Content $manifestFile)
$xmlDoc.manifest.versionCode = $version.Build.ToString()
$xmlDoc.save($manifestFile);

# bundle (aab)
& $msbuild $projectFile /p:Configuration=Release /t:Clean
& $msbuild $projectFile /p:Configuration=Release /t:SignAndroidPackage /p:Version=$versionParam /p:ArchiveOnBuild=true `
	/p:AndroidKeyStore=True /p:AndroidSigningKeyStore=$keystore /p:AndroidSigningKeyAlias=$keystoreAlias /p:AndroidSigningKeyPass=$keystorePass /p:AndroidSigningStorePass=$keystorePass 

# apk
& $msbuild $projectFile /p:Configuration=Release /t:Clean /p:OutputPath="bin\ReleaseApk"
& $msbuild $projectFile /p:Configuration=Release /t:SignAndroidPackage  /p:Version=$versionParam /p:OutputPath="bin\ReleaseApk" /p:AndroidPackageFormat="apk" `
	/p:AndroidSigningKeyStore=$keystore /p:AndroidSigningKeyAlias=$keystoreAlias /p:AndroidSigningStorePass=$keystorePass /p:JarsignerTimestampAuthorityUrl="https://freetsa.org/tsr"

# report version
ReportVersion


# paths
$solutionDir = Split-Path -parent $PSScriptRoot;
$projectDir = $PSScriptRoot
$msbuild = Join-Path ${Env:ProgramFiles(x86)} "Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;

$credentials = (Get-Content "$solutionDir\..\.user\credentials.json" | Out-String | ConvertFrom-Json);
$versionBase = (Get-Content "$solutionDir\Pub\version.json" | Out-String | ConvertFrom-Json);
$versionBaseDate = [datetime]::new($versionBase.BaseYear, 1, 1);
$versionMajor = $versionBase.Major;
$versionMinor = $versionBase.Minor;

# find current version
$timeSpan = [datetime]::Now - $versionBaseDate;
$version = [version]::new($versionMajor, $versionMinor, $timeSpan.Days, $timeSpan.Hours * 60 + $timeSpan.Minutes);
$versionParam = $version.ToString();

# android
$keystore = Join-Path "$solutionDir\..\.user\" $credentials.Android.KeyStoreFile
$keystorePass = $credentials.Android.KeyStorePass
$keystoreAlias = $credentials.Android.KeyStoreAlias
$apkPublishDir = Join-Path $projectDir "bin\release\publish";
$signedApk= Join-Path $apkPublishDir "com.vpnhood.client.android-signed.apk"

& $msbuild $projectFile /p:Configuration=Release /t:Clean
& $msbuild $projectFile /p:Configuration=Release /t:SignAndroidPackage /p:Version=$versionParam /p:ArchiveOnBuild=true `
	/p:AndroidKeyStore=True /p:AndroidSigningKeyStore=$keystore /p:AndroidSigningKeyAlias=$keystoreAlias /p:AndroidSigningKeyPass=$keystorePass /p:AndroidSigningStorePass=$keystorePass 


& $msbuild $projectFile /p:Configuration=Release /t:Clean /p:OutputPath="bin\ReleaseApk"
& $msbuild $projectFile /p:Configuration=Release /t:SignAndroidPackage  /p:Version=$versionParam /p:OutputPath="bin\ReleaseApk" /p:AndroidPackageFormat="apk" `
	/p:AndroidSigningKeyStore=$keystore /p:AndroidSigningKeyAlias=$keystoreAlias /p:AndroidSigningStorePass=$keystorePass /p:JarsignerTimestampAuthorityUrl="https://freetsa.org/tsr"

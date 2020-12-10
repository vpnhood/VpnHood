
# paths
$solutionDir = Split-Path -parent $PSScriptRoot;
$projectDir = $PSScriptRoot
$msbuild = Join-Path ${Env:ProgramFiles(x86)} "Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;

$credentials = (Get-Content "$solutionDir\..\.user\credentials.json" | Out-String | ConvertFrom-Json);

# version
$versionFile = Join-Path $solutionDir "\Pub\version.json"
$versionJson = (Get-Content $versionFile | Out-String | ConvertFrom-Json);
$bumpTime = [datetime]::Parse($versionJson.BumpTime);
$isVersionBumped=( ((Get-Date)-$bumpTime).TotalMinutes -ge 5);
if ( $isVersionBumped )
{
	$versionJson.Build = $versionJson.Build + 1;
	$versionJson.BumpTime = [datetime]::UtcNow.ToString("o");
	$versionJson | ConvertTo-Json -depth 10 | Out-File $versionFile;
}
$version=[version]::new($versionJson.Major, $versionJson.Minor, $versionJson.Build);
$versionParam = $version.ToString(3);

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
if ($isVersionBumped)
{
	Write-Host "New version: $versionParam" -ForegroundColor GREEN
}
else
{
	Write-Host "OLD Version: $versionParam" -ForegroundColor Yellow -BackgroundColor Red
}

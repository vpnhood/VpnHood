param(
	[Parameter(Mandatory=$true)] [String]$projectDir, 
	[Parameter(Mandatory=$true)] [String]$packageFileTitle,
	[Parameter(Mandatory=$true)] [String]$packageId,
	[Parameter(Mandatory=$true)] [String]$distribution,
	[switch]$apk, [switch]$aab)

. "$PSScriptRoot/Common.ps1"

$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;

Write-Host "";
Write-Host "*** Publishing $projectFile ..." -BackgroundColor Blue -ForegroundColor White;

#update project version
UpdateProjectVersion $projectFile;

# prepare module folders
$moduleDir = "$packagesRootDir/$packageFileTitle/android-$distribution";
$moduleDirLatest = "$packagesRootDirLatest/$packageFileTitle/android-$distribution";
PrepareModuleFolder $moduleDir $moduleDirLatest;

$packageExt = if ($apk) { "apk" } else { "aab" };
$module_infoFile = "$moduleDir/$packageFileTitle-android-$distribution.json";
$module_packageFile = "$moduleDir/$packageFileTitle-android-$distribution.$packageExt";

# Calcualted Path
$module_infoFileName = $(Split-Path "$module_infoFile" -leaf);
$module_packageFileName = $(Split-Path "$module_packageFile" -leaf);

# android
$nodeName = "Android.$packageFileTitle.$distribution";
$keystore = Join-Path "$solutionDir/../.user/" $credentials.$nodeName.KeyStoreFile
$keystorePass = $credentials.$nodeName.KeyStorePass
$keystoreAlias = $credentials.$nodeName.KeyStoreAlias
$manifestFile = Join-Path $projectDir "Properties/AndroidManifest.xml";
$appIconXml = Join-Path $projectDir "Resources/mipmap-anydpi-v26/ic_launcher.xml";
$appIconXmlDoc = [xml](Get-Content $appIconXml);
$appIconXmlNode = $appIconXmlDoc.selectSingleNode("adaptive-icon/background");

Write-Host;
Write-Host "*** Creating $module_packageFileName ..." -BackgroundColor Blue -ForegroundColor White;

# ------------- apk
if ($apk)
{
	$outputPath = Join-Path $projectDir "bin/Release-$distribution/";
	$signedPacakgeFile = Join-Path $outputPath "$packageId-Signed.apk"
	dotnet build $projectFile /t:Clean /t:SignAndroidPackage /verbosity:$msverbosity `
		/p:SolutionDir=$solutionDir `
		/p:Configuration=Release `
		/p:ApplicationId=$packageId `
		/p:Version=$versionParam `
		/p:OutputPath=$outputPath `
		/p:ArchiveOnBuild=true `
		/p:AndroidPackageFormat="apk" `
		/p:AndroidSigningKeyStore=$keystore /p:AndroidSigningKeyAlias=$keystoreAlias /p:AndroidSigningStorePass=$keystorePass `
		/p:AndroidSigningKeyPass=$keystorePass /p:AndroidKeyStore=True `
		/nodeReuse:false;
	
	if ($LASTEXITCODE -gt 0) { Throw "The build exited with error code: " + $lastexitcode; }
	 
	# publish info
	$json = @{
		Version = $versionParam; 
		UpdateInfoUrl = "https://github.com/vpnhood/VpnHood/releases/latest/download/$module_infoFileName";
		PackageUrl = "https://github.com/vpnhood/VpnHood/releases/download/$versionTag/$module_packageFileName";
		InstallationPageUrl = "https://github.com/vpnhood/VpnHood/releases/download/$versionTag/$module_packageFileName";
		ReleaseDate = "$releaseDate";
		DeprecatedVersion = "$deprecatedVersion";
		NotificationDelay = "03:00:00";
	};

	$json | ConvertTo-Json | Out-File $module_infoFile -Encoding ASCII;
}

# ------------- aab
if ($aab)
{
	# set app icon
	$appIconXmlNode.SetAttribute("android:drawable", "@drawable/ic_launcher_background");
	$appIconXmlDoc.save($appIconXml);

	# update variables
	$outputPath = Join-Path $projectDir "bin/ReleaseAab/";
	$signedPacakgeFile = Join-Path "$outputPath" "$packageId-Signed.aab"
	$module_packageFile = "$moduleDir/$packageFileTitle-android.aab";
	$module_packageFileName = $(Split-Path "$module_packageFile" -leaf);

	dotnet build $projectFile /t:Clean /t:SignAndroidPackage /verbosity:$msverbosity `
		/p:SolutionDir=$solutionDir `
		/p:Configuration=Release `
		/p:ApplicationId=$packageId `
		/p:Version=$versionParam `
		/p:OutputPath=$outputPath `
		/p:ArchiveOnBuild=true `
		/p:AndroidSigningKeyStore=$keystore /p:AndroidSigningKeyAlias=$keystoreAlias /p:AndroidSigningStorePass=$keystorePass `
		/p:AndroidSigningKeyPass=$keystorePass /p:AndroidKeyStore=True `
		/nodeReuse:false;

	if ($LASTEXITCODE -gt 0) { Throw "The build exited with error code: " + $lastexitcode; }
}

# copy to module
Copy-Item -path $signedPacakgeFile -Destination $module_packageFile -Force

if ($isLatest)
{
	Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse;
}

# report version
ReportVersion
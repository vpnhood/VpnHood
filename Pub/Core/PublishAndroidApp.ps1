param(
	[Parameter(Mandatory=$true)] [String]$projectDir, 
	[Parameter(Mandatory=$true)] [String]$packageFileTitle,
	[Parameter(Mandatory=$true)] [String]$packageId,
	[Parameter(Mandatory=$true)] [String]$distribution,
	[Parameter(Mandatory=$true)] [String]$repoUrl,
	[switch]$apk, [switch]$aab)

. "$PSScriptRoot/Common.ps1"
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;

Write-Host "";
Write-Host "*** Publishing $projectFile ..." -BackgroundColor Blue -ForegroundColor White;

# set default Rollout to 100 if not set
if ($rollout -le 0 -or $rollout -gt 100) { $rollout = 100; }

#update project version
UpdateProjectVersion $projectFile;

# prepare module folders
$moduleDir = "$packagesRootDir/$packageFileTitle/android-$distribution";
$moduleDirLatest = "$packagesRootDirLatest/$packageFileTitle/android-$distribution";
PrepareModuleFolder $moduleDir $moduleDirLatest;

$packageExt = if ($apk) { "apk" } else { "aab" };
$module_baseFileName = "$packageFileTitle-android-$distribution";
$module_infoFile = "$moduleDir/$module_baseFileName.json";
$module_packageFile = "$moduleDir/$module_baseFileName.$packageExt";

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
	# not-sure about RestoreDisableParallel yet
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
		;
	
	if ($LASTEXITCODE -gt 0) { Throw "The build exited with error code: " + $lastexitcode; }

	# publish info
	$json = @{
		Version = $versionParam; 
		UpdateInfoUrl = "$repoUrl/releases/latest/download/$module_infoFileName";
		PackageUrl = "$repoUrl/releases/download/$versionTag/$module_packageFileName";
		PackageId = "$packageId";
		InstallationPageUrl = "$repoUrl/releases/download/$versionTag/$module_packageFileName";
		ReleaseDate = "$releaseDate";
		DeprecatedVersion = "$deprecatedVersion";
		NotificationDelay = "$versionNotificationDelay";
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
		;

	if ($LASTEXITCODE -gt 0) { Throw "The build exited with error code: " + $lastexitcode; }

	# rollout fraction 0.00 - 1.00 with two decimals
	$rolloutRatio = "{0:F2}" -f ([double]$rollout / 100);

	# publish info
	$json = @{
		Version = $versionParam; 
		UpdateInfoUrl = "$repoUrl/releases/latest/download/$packageFileTitle-android.json";
		PackageUrl = "$repoUrl/releases/download/$versionTag/$packageFileTitle-android.apk";
		PackageId = "$packageId";
		InstallationPageUrl = "$repoUrl/releases/download/$versionTag/$packageFileTitle-android.apk";
		ReleaseDate = "$releaseDate";
		DeprecatedVersion = "$deprecatedVersion";
		NotificationDelay = "7.00:00:00";
		Rollout = $rolloutRatio;
		GooglePlayUrl = "https://play.google.com/store/apps/details?id=$packageId";
	};

	$json | ConvertTo-Json | Out-File "$module_packageFile.json" -Encoding ASCII;
}

# copy to module
Copy-Item -path $signedPacakgeFile -Destination $module_packageFile -Force

if ($isLatest)
{
	Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse;
}

# report version
ReportVersion
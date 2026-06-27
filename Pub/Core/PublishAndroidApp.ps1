param(
	[Parameter(Mandatory=$true)] [String]$projectDir, 
	[Parameter(Mandatory=$true)] [String]$packageFileTitle,
	[Parameter(Mandatory=$true)] [String]$packageId,
	[Parameter(Mandatory=$true)] [String]$distribution,
	[Parameter(Mandatory=$true)] [String]$repoUrl,
	[String]$archs = "",
	[switch]$apk, [switch]$aab)

. "$PSScriptRoot/Common.ps1"

# clean up any leftover temp project files
Get-ChildItem -Path $projectDir -File -Filter "*.tmp.csproj" | Remove-Item -Force;

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

# android signing — resolve from the non-secret manifest + per-key secrets in .user/<dir>/
# (keystore.p12 + keystore_pass.txt). The manifest handles distribution fan-out so secrets
# are never duplicated; CI materializes the same files from GitHub secrets.
$signing = (Get-Content "$PSScriptRoot/android-signing.json" -Raw | ConvertFrom-Json)."$packageFileTitle.$distribution";
if ($null -eq $signing) { Throw "No android-signing.json entry for '$packageFileTitle.$distribution'." }
$keystoreDir = Join-Path "$solutionDir/../.user/" $signing.dir
$keystore = Join-Path $keystoreDir "keystore.p12"
$keystorePass = (Get-Content (Join-Path $keystoreDir "keystore_pass.txt") -Raw).Trim()
# Key alias resolution, in priority order:
#   1. explicit "alias" in android-signing.json (override),
#   2. keystore_alias.txt next to the keystore (written locally / by CI),
#   3. auto-detect from the keystore.
# Auto-detect (3) is valid ONLY when the keystore holds exactly one PrivateKeyEntry (only a key
# entry can sign); on 0 or >1 it fails and asks for an explicit alias. A fork can therefore drop in
# a bare keystore (auto-detect) or ship an alias file, without editing the repo.
$keystoreAlias = $signing.alias
$aliasFile = Join-Path $keystoreDir "keystore_alias.txt"
if ([string]::IsNullOrWhiteSpace($keystoreAlias) -and (Test-Path $aliasFile)) {
	$keystoreAlias = (Get-Content $aliasFile -Raw).Trim()
}
if ([string]::IsNullOrWhiteSpace($keystoreAlias)) {
	$keyAliases = @()
	$current = $null
	foreach ($line in (& keytool -list -v -keystore $keystore -storepass $keystorePass 2>&1)) {
		$s = $line.ToString()
		if ($s -match '^Alias name:\s*(.+?)\s*$') { $current = $Matches[1] }
		elseif ($s -match '^Entry type:\s*PrivateKeyEntry' -and $current) { $keyAliases += $current; $current = $null }
	}
	if ($keyAliases.Count -eq 0) {
		Throw "No PrivateKeyEntry found in '$keystore' (add an explicit alias to android-signing.json)."
	}
	if ($keyAliases.Count -gt 1) {
		Throw "Keystore '$keystore' has multiple key entries ($($keyAliases -join ', ')); set an explicit 'alias' for '$packageFileTitle.$distribution' in android-signing.json."
	}
	$keystoreAlias = $keyAliases[0]
}
$manifestFile = Join-Path $projectDir "Properties/AndroidManifest.xml";
$appIconXml = Join-Path $projectDir "Resources/mipmap-anydpi-v26/ic_launcher.xml";
$appIconXmlDoc = [xml](Get-Content $appIconXml);
$appIconXmlNode = $appIconXmlDoc.selectSingleNode("adaptive-icon/background");
if ([string]::IsNullOrWhiteSpace($archs)) {
    $archs = "android-arm64;android-x64;android-arm;"
}

# prepare temp project file and update RuntimeIdentifiers
# I got sick and tird passing -p:RuntimeIdentifiers 
$tempProjectFile = $projectFile + ".tmp.csproj"
Copy-Item -Path $projectFile -Destination $tempProjectFile -Force

$projectXml = New-Object System.Xml.XmlDocument
$projectXml.PreserveWhitespace = $true
$projectXml.Load($tempProjectFile)

$runtimeIdentifierNodes = $projectXml.SelectNodes("/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='RuntimeIdentifiers']")
foreach ($runtimeIdentifierNode in $runtimeIdentifierNodes) {
	$runtimeIdentifierNode.InnerText = $archs
}
$projectXml.Save($tempProjectFile)

Write-Host;
Write-Host "*** Creating $module_packageFileName ..." -BackgroundColor Blue -ForegroundColor White;

try {
	# ------------- apk
	if ($apk)
	{
		# not-sure about RestoreDisableParallel yet
		$outputPath = Join-Path $projectDir "bin/Release-$distribution/";
		$signedPacakgeFile = Join-Path $outputPath "$packageId-Signed.apk"
		dotnet build $tempProjectFile /t:Clean /t:SignAndroidPackage /verbosity:$msverbosity `
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

		dotnet build $tempProjectFile /t:Clean /t:SignAndroidPackage /verbosity:$msverbosity `
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
}
finally {
	# delete *.tmp.csproj.nuget.* in bin folders
	Get-ChildItem -Path (Join-Path $projectDir "obj") -Recurse -File -Filter "*.tmp.csproj.nuget.*" -ErrorAction SilentlyContinue | Remove-Item -Force;

	if (Test-Path $tempProjectFile) {
		Remove-Item -Path $tempProjectFile -Force
	}
}

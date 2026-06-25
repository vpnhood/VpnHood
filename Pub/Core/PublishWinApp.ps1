param(
	[Parameter(Mandatory=$true)] [String]$projectDir,
	[Parameter(Mandatory=$true)] [String]$packageFileTitle,
	[Parameter(Mandatory=$true)] [String]$aipFileR,
	[Parameter(Mandatory=$true)] [String]$distribution,
	[Parameter(Mandatory=$true)] [String]$repoUrl,
	[Parameter(Mandatory=$true)] [String]$installationPageUrl,
	# Which phase to run. "all" (default) does the full local flow in one process.
	# CI splits it into two separately-labeled steps: "publish" compiles the binary,
	# "package" wraps it in the MSI. Both stages share the same computed paths and the
	# on-disk $publishDir, so they can run as two processes on the same machine.
	[Parameter(Mandatory=$false)] [ValidateSet("all", "publish", "package")] [String]$stage = "all"
)

. "$PSScriptRoot/Common.ps1"

$doPublish = $stage -in @("all", "publish");
$doPackage = $stage -in @("all", "package");

# --- Shared values (computed in every stage so each process is self-contained) ---
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;
$productName = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.Product[0];
$assemblyName = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.AssemblyName[0];
$targetFramework = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.TargetFramework;
$publishDir = "$projectDir/bin/Publish-$distribution";
$aipFile = "$solutionDir/$aipFileR";
$aipFolder = Split-Path -parent $aipFile;

# module paths
$moduleDir = "$packagesRootDir/$packageFileTitle/windows-$distribution";
$moduleDirLatest = "$packagesRootDirLatest/$packageFileTitle/windows-$distribution";
$module_infoFile = "$moduleDir/$packageFileTitle-win-x64.json";
$module_packageFile = [System.IO.Path]::ChangeExtension($module_infoFile, ".msi");
$module_updaterConfigFile = [System.IO.Path]::ChangeExtension($module_infoFile, ".txt");
$module_infoFileName = $(Split-Path "$module_infoFile" -leaf);
$module_packageFileName = $(Split-Path "$module_packageFile" -leaf);

# --- Optional code signing (Microsoft Trusted Signing) ---
# Runs ONLY when all signing credentials are provided via environment: the Azure
# service-principal vars plus the VpnHood Trusted Signing target. Without them the
# build is intentionally UNSIGNED and does not fail. When signing IS configured, any
# failure is fatal so a silently-unsigned package is never shipped.
$signEnabled = [bool]($env:AZURE_TENANT_ID -and $env:AZURE_CLIENT_ID -and $env:AZURE_CLIENT_SECRET `
	-and $env:VH_SIGN_ACCOUNT -and $env:VH_SIGN_PROFILE -and $env:VH_SIGN_ENDPOINT);
$script:signToolReady = $false;
function Invoke-VhSign([string[]]$files) {
	if (-not $signEnabled) { return; }
	if (-not $script:signToolReady) {
		dotnet tool install --global sign 2>$null | Out-Null;  # idempotent (no-op if already installed)
		$script:signToolReady = $true;
	}
	Write-Host "Signing via Trusted Signing: $($files -join ', ')" -ForegroundColor Cyan;
	sign code trusted-signing $files `
		--trusted-signing-account "$env:VH_SIGN_ACCOUNT" `
		--trusted-signing-certificate-profile "$env:VH_SIGN_PROFILE" `
		--trusted-signing-endpoint "$env:VH_SIGN_ENDPOINT";
	if ($LASTEXITCODE -ne 0) { Throw "Code signing failed (exit $LASTEXITCODE)."; }
}
if (-not $signEnabled) { Write-Host "Code signing skipped: no signing credentials configured (unsigned build)." -ForegroundColor Yellow; }

# =====================================================================================
# Stage: publish — compile the self-contained Windows binary
# =====================================================================================
if ($doPublish) {
	Write-Host;
	Write-Host "*** [publish] Building $packageFileTitle binary for Windows ..." -BackgroundColor Blue -ForegroundColor White;

	#update project version
	UpdateProjectVersion $projectFile;

	# publish
	# NOTE: appSettings will not load from private files if p:SolutionDir=$solutionDir is not set
	dotnet publish $projectDir `
		/p:SolutionDir=$solutionDir `
		/p:Configuration=Release `
		/p:Version=$versionParam `
		--output $publishDir `
		--framework $targetFramework `
		--self-contained `
		--runtime "win-x64";

	if ($LASTEXITCODE -gt 0) { Throw "The publish exited with error code: " + $lastexitcode; }

	# sign the published executable before packaging, so the MSI contains a signed exe
	Invoke-VhSign @("$publishDir/$assemblyName.exe");
}

# =====================================================================================
# Stage: package — wrap the published binary into the MSI with Advanced Installer
# =====================================================================================
if ($doPackage) {
	Write-Host;
	Write-Host "*** [package] Building MSI for $packageFileTitle ..." -BackgroundColor Blue -ForegroundColor White;

	# Locate AdvancedInstaller.com. In CI the Caphyon action exposes the install via the
	# AdvancedInstallerRoot env var (no registry key); locally it's found via the registry.
	$advinstallerFile = $null;
	if ($env:AdvancedInstallerRoot) {
		$advinstallerFile = Get-ChildItem -Path $env:AdvancedInstallerRoot -Recurse -Filter "AdvancedInstaller.com" -ErrorAction SilentlyContinue |
			Select-Object -First 1 -ExpandProperty FullName;
	}
	if (-not $advinstallerFile) {
		$advinstallerRoot = (Get-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Caphyon\Advanced Installer" -Name "InstallRoot").InstallRoot;
		$advinstallerFile = Join-Path $advinstallerRoot "bin\x86\AdvancedInstaller.com";
	}
	if (-not $advinstallerFile -or -not (Test-Path $advinstallerFile)) {
		throw "Could not locate AdvancedInstaller.com (AdvancedInstallerRoot='$env:AdvancedInstallerRoot').";
	}

	# prepare module folders
	PrepareModuleFolder $moduleDir $moduleDirLatest;

	# Build Setup
	$buildPacakgeFile = "$aipFolder/release/$packageFileTitle-win-x64.msi";
	& $advinstallerFile /build "$aipFile";
	if ($LASTEXITCODE -ne 0) { Throw "AdvancedInstaller build failed (exit $LASTEXITCODE)."; }

	# sign the built installer
	Invoke-VhSign @("$buildPacakgeFile");

	#####
	# copy to module
	Copy-Item -path "$buildPacakgeFile" -Destination "$moduleDir/" -Force;

	# publish info
	$json = @{
		Version = $versionParam;
		UpdateInfoUrl = "$repoUrl/releases/latest/download/$module_infoFileName";
		PackageUrl = "$repoUrl/releases/download/$versionTag/$module_packageFileName";
		InstallationPageUrl = "$installationPageUrl";
		ReleaseDate = "$releaseDate";
		DeprecatedVersion = "$deprecatedVersion";
		NotificationDelay = "$versionNotificationDelay";
	};
	$json | ConvertTo-Json | Out-File "$module_infoFile" -Encoding ASCII;

	# Create Updater Config File
	$str=";aiu;

[Update]
Name = $productName $versionParam
ProductVersion = $versionParam
URL = $repoUrl/releases/download/$versionTag/$module_packageFileName
Size = $((Get-Item $module_packageFile).length)
SHA256 = $((Get-FileHash $module_packageFile -Algorithm SHA256).Hash)
MD5 = $((Get-FileHash $module_packageFile -Algorithm MD5).Hash)
ServerFileName = $module_packageFileName
Flags = NoRedetect
RegistryKey = HKUD\Software\$assemblyName\$packageFileTitle\Version
Version = $versionParam
UpdatedApplications = $productName(1.0-$versionParam)
Description = <a href=""https://github.com/vpnhood/VpnHood/blob/main/CHANGELOG.md"">Release note</a>
";
	$str | Out-File -FilePath $module_updaterConfigFile;

	if ($isLatest)
	{
		Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse;
	}
}

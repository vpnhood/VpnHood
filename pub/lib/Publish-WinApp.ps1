param(
	[Parameter(Mandatory=$true)] [String]$projectDir,
	# The .user/<appFolder>/ config folder name; also the bin module dir name and default artifact title.
	[Parameter(Mandatory=$true)] [String]$appFolder,
	[Parameter(Mandatory=$true)] [String]$aipFileR,
	[Parameter(Mandatory=$true)] [String]$distribution,
	# User-facing install/download page baked into the publish JSON. Read from .user; an explicit value
	# here still wins, and absent both it defaults to the repo's releases page.
	[Parameter(Mandatory=$false)] [String]$installationPageUrl = "",
	# Release repo for Connect (VH_CONNECT_PUBLISH_REPO) vs client; the URL itself is resolved below.
	[switch]$connect,
	# Which phase to run. "all" (default) does the full local flow in one process.
	# CI splits it into two separately-labeled steps: "publish" compiles the binary,
	# "package" wraps it in the MSI. Both stages share the same computed paths and the
	# on-disk $publishDir, so they can run as two processes on the same machine.
	[Parameter(Mandatory=$false)] [ValidateSet("all", "publish", "package")] [String]$stage = "all"
)

. "$PSScriptRoot/Common.ps1"

# Emit a warning to the console (yellow, so it is visible on a local command-line build) and, when
# running under GitHub Actions, ALSO as a build annotation (::warning::) so it surfaces on the run.
function Write-VhBuildWarning([string]$message, [string]$title = "") {
	if ($env:GITHUB_ACTIONS -eq "true") {
		$t = if ($title) { " title=$title" } else { "" };
		Write-Host "::warning$t::$message";
	}
	Write-Warning $message;
}

# Per-app config from .user/<appFolder>/publish.json (RepoUrl + PackageTitle; no packageId on Windows).
# The optional title override only renames the published artifacts; .user/module lookups stay keyed by
# $appFolder. See AppPublishConfig.ps1.
$appConfig = Get-AppPublishConfig $appFolder;
$packageFileTitle = if ($appConfig.packageFileTitle) { $appConfig.packageFileTitle } else { $appFolder }
$repoUrl = if ($appConfig.repoUrl) { $appConfig.repoUrl } else { Resolve-PublishRepoUrl -Connect:$connect };
$installationPageUrl =
	if ($appConfig.installationPageUrl) { $appConfig.installationPageUrl }
	elseif (-not [string]::IsNullOrWhiteSpace($installationPageUrl)) { $installationPageUrl }
	else { "$repoUrl/releases/latest" };
# Strict: the app's shared appsettings (embedded as AppSettings.json) must exist when strict.
Assert-AppSettings $appFolder;
# Strict: Connect must carry a default server access key. Windows is a direct download, so it shares the
# 'web' key ($distribution is already "web" here) with the Android web APK.
Assert-DefaultAccessKey $appFolder $distribution -Connect:$connect;

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

# module paths (dir keyed by the stable app folder; file names use the artifact title)
$moduleDir = "$packagesRootDir/$appFolder/windows-$distribution";
$moduleDirLatest = "$packagesRootDirLatest/$appFolder/windows-$distribution";
$module_infoFile = "$moduleDir/$packageFileTitle-win-x64.json";
$module_packageFile = [System.IO.Path]::ChangeExtension($module_infoFile, ".msi");
$module_updaterConfigFile = [System.IO.Path]::ChangeExtension($module_infoFile, ".txt");
$module_infoFileName = $(Split-Path "$module_infoFile" -leaf);
$module_packageFileName = $(Split-Path "$module_packageFile" -leaf);

# --- Optional code signing (Azure Trusted/Artifact Signing) ---
# SignFiles.ps1 resolves AZURE_SIGNING_CREDENTIAL / AZURE_SIGNING_TARGET (secrets in CI, .user/
# files locally), enforces the all-or-nothing pair rule, and provides $signEnabled + Invoke-VhSign.
# Shared with Publish-NugetPackages.ps1 so app binaries and NuGet assemblies sign identically.
. "$PSScriptRoot/SignFiles.ps1"

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

	# Sign every published binary before packaging, so the MSI carries signed content. The publish is
	# self-contained and NOT single-file, so "$assemblyName.exe" is only the apphost stub: all of our own
	# code lives in the sibling VpnHood*.dll files. Signing the exe alone made the installer look signed
	# while every managed assembly it loads stayed unverifiable.
	# User-mode binaries only. Kernel drivers (WinDivert64.sys and its catalog) must KEEP their Microsoft
	# attestation signature: re-signing a driver with our cert makes Windows refuse to load it. Do not
	# widen this list to "*" or add .sys/.cat. Already-signed files are dropped inside Invoke-VhSign,
	# so the vendor-signed .NET runtime and WinDivert.dll pass straight through untouched.
	Invoke-VhSign (Get-ChildItem $publishDir -File -Recurse -Include "*.exe", "*.dll" |
		Select-Object -ExpandProperty FullName);

	# The apphost is the file Windows shows in the UAC/SmartScreen prompt, so it must never slip through
	# unsigned when signing is configured (a renamed AssemblyName would silently miss the glob above).
	if ($signEnabled -and -not (Get-AuthenticodeSignature "$publishDir/$assemblyName.exe").SignerCertificate) {
		Throw "Signing is configured but '$assemblyName.exe' is still unsigned after the signing step.";
	}
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

	# Verify the installer actually carries a signature and surface it explicitly. Signing is optional
	# (see $signEnabled) so an unsigned build is NOT fatal — but it must never pass silently: warn on the
	# command line (local builds) and as a GitHub annotation (CI). When signing is configured yet the
	# file is still unsigned, the signing step misbehaved, so the warning is worded differently.
	$sig = Get-AuthenticodeSignature $buildPacakgeFile;
	if ($sig.SignerCertificate) {
		# A certificate is embedded -> the file IS signed. Status may be non-'Valid' on the build agent
		# (e.g. a short-lived Trusted Signing cert whose chain isn't validated locally); that is not an
		# "unsigned" condition, so just report it.
		$note = if ($sig.Status -eq 'Valid') { "Valid" } else { "signed (Authenticode status: $($sig.Status))" };
		Write-Host "MSI signature: $note ($($sig.SignerCertificate.Subject))." -ForegroundColor Green;
	}
	elseif (-not $signEnabled) {
		Write-VhBuildWarning ("The Windows installer '$module_packageFileName' is UNSIGNED: Azure Trusted Signing is not " +
			"configured. Set AZURE_SIGNING_CREDENTIAL and AZURE_SIGNING_TARGET (see .github/DEPLOYMENT.md) to sign the build.") `
			"Windows build is unsigned — Azure signing not configured";
	}
	else {
		Write-VhBuildWarning ("The Windows installer '$module_packageFileName' is UNSIGNED even though signing is " +
			"configured — the signing step produced no signature.") `
			"Windows installer failed to sign";
	}

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

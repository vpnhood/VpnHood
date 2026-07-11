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

# --- Resolve the Azure signing credential from a single consolidated source ----------
# The Azure service principal is supplied as ONE JSON credential — exactly the file you download
# from Azure (e.g. `az ad sp create-for-rbac ...`). In CI it arrives as the single
# AZURE_SIGNING_CREDENTIAL secret; locally it's read from .user/azure_signing_credential.json. It
# carries AZURE_TENANT_ID / AZURE_CLIENT_ID / AZURE_CLIENT_SECRET (any other fields are ignored).
# The Trusted Signing target (Endpoint/CodeSigningAccountName/CertificateProfileName) is NOT part of this file and stays
# separate. Explicit AZURE_* env vars (if already set) win; this only fills them in when absent.
if (-not ($env:AZURE_TENANT_ID -and $env:AZURE_CLIENT_ID -and $env:AZURE_CLIENT_SECRET)) {
	$azCredRaw =
		if ($env:AZURE_SIGNING_CREDENTIAL) { $env:AZURE_SIGNING_CREDENTIAL }
		elseif (Test-Path "$userDir/azure_signing_credential.json") { Get-Content "$userDir/azure_signing_credential.json" -Raw }
		else { $null };
	if ($azCredRaw) {
		try { $azCred = $azCredRaw | ConvertFrom-Json }
		catch { Throw "Azure signing credential is not valid JSON: $($_.Exception.Message)"; }
		foreach ($k in "AZURE_TENANT_ID", "AZURE_CLIENT_ID", "AZURE_CLIENT_SECRET") {
			if ($azCred.$k) {
				# Register the value as a masked secret in CI logs before it lands in the process env.
				if ($env:GITHUB_ACTIONS -eq "true") { Write-Host "::add-mask::$($azCred.$k)"; }
				Set-Item -Path "Env:$k" -Value ([string]$azCred.$k);
			}
		}
	}
}

# --- Resolve the Trusted Signing target from a single consolidated source ------------
# The Trusted Signing target is supplied as ONE JSON in Azure's own metadata.json schema (the file
# signtool's dlib consumes): { Endpoint, CodeSigningAccountName, CertificateProfileName }. In CI it
# arrives as the AZURE_SIGNING_TARGET variable; locally it's read from .user/azure_signing_target.json.
# These are identifiers (not the Azure secret).
$signAccount = $null; $signProfile = $null; $signEndpoint = $null;
$signTargetRaw =
	if ($env:AZURE_SIGNING_TARGET) { $env:AZURE_SIGNING_TARGET }
	elseif (Test-Path "$userDir/azure_signing_target.json") { Get-Content "$userDir/azure_signing_target.json" -Raw }
	else { $null };
if ($signTargetRaw) {
	try { $signTarget = $signTargetRaw | ConvertFrom-Json }
	catch { Throw "AZURE_SIGNING_TARGET is not valid JSON: $($_.Exception.Message)"; }
	$signAccount  = $signTarget.CodeSigningAccountName;
	$signProfile  = $signTarget.CertificateProfileName;
	$signEndpoint = $signTarget.Endpoint;
}

# --- Optional code signing (Microsoft Trusted Signing) ---
# Runs ONLY when the signing credentials are provided: the Azure service principal (resolved above
# from AZURE_SIGNING_CREDENTIAL) plus the Trusted Signing target (from AZURE_SIGNING_TARGET). Without
# them the build is intentionally UNSIGNED and does not fail. When signing IS configured, any
# failure is fatal so a silently-unsigned package is never shipped.
$signEnabled = [bool]($env:AZURE_TENANT_ID -and $env:AZURE_CLIENT_ID -and $env:AZURE_CLIENT_SECRET `
	-and $signAccount -and $signProfile -and $signEndpoint);
$script:signToolReady = $false;
function Invoke-VhSign([string[]]$files) {
	if (-not $signEnabled) { return; }
	if (-not $script:signToolReady) {
		# Ensure the dotnet global-tools dir is on PATH for THIS process — a freshly
		# installed global tool is not added to the current process's PATH automatically.
		$toolsDir = if ($env:USERPROFILE) { Join-Path $env:USERPROFILE ".dotnet\tools" } else { Join-Path $env:HOME ".dotnet/tools" };
		$sep = [IO.Path]::PathSeparator;
		if (($env:PATH -split [regex]::Escape($sep)) -notcontains $toolsDir) { $env:PATH = "$toolsDir$sep$env:PATH"; }
		# Install the Microsoft 'sign' CLI. It ships ONLY as prerelease NuGet versions,
		# so --prerelease is required. Capture output so a failure is diagnosable.
		$installLog = dotnet tool install --global sign --prerelease 2>&1;
		if (-not (Get-Command sign -ErrorAction SilentlyContinue)) {
			Write-Host ($installLog -join "`n");
			Throw "The 'sign' CLI is not available after 'dotnet tool install --global sign --prerelease' (check PATH/install).";
		}
		$script:signToolReady = $true;
	}
	Write-Host "Signing via Trusted Signing: $($files -join ', ')" -ForegroundColor Cyan;
	sign code trusted-signing $files `
		--trusted-signing-account "$signAccount" `
		--trusted-signing-certificate-profile "$signProfile" `
		--trusted-signing-endpoint "$signEndpoint";
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

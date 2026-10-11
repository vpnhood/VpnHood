param(
	[Parameter(Mandatory=$true)] [String]$projectDir,
	# The .user/<appFolder>/ config folder name; also the bin module dir name and default artifact title.
	[Parameter(Mandatory=$true)] [String]$appFolder,
	[Parameter(Mandatory=$true)] [String]$distribution,
	# The upgrade code of the MSI the app's earlier releases shipped, which the setup removes first; a
	# product that never shipped one passes none.
	[Parameter(Mandatory=$false)] [String]$legacyMsiUpgradeCode = "",
	# User-facing install/download page baked into the publish JSON. Read from .user; an explicit value
	# here still wins, and absent both it defaults to the repo's releases page.
	[Parameter(Mandatory=$false)] [String]$installationPageUrl = "",
	# Release repo for Connect (VH_CONNECT_PUBLISH_REPO) vs client; the URL itself is resolved below.
	[switch]$connect,
	# Which phase to run. "all" (default) does the full local flow in one process.
	# CI splits it into two separately-labeled steps: "publish" compiles the binary,
	# "package" wraps it in the NSIS setup. Both stages share the same computed paths and the
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

# Per-app config from .user/<appFolder>/publish.json (RepoUrl, InstallationPageUrl; no package id on
# Windows). The published files are named after $appFolder, the app's package title. See
# AppPublishConfig.ps1.
$appConfig = Get-AppPublishConfig $appFolder;
$packageFileTitle = $appFolder;
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
# Both from the app's identity, so as MSBuild evaluates them (Get-ProjectProperty).
$productName = Get-ProjectProperty $projectFile "Product";
$assemblyName = Get-ProjectProperty $projectFile "AssemblyName";
# the console launcher's name; the build writes it beside the window's exe (VhAppCommandName)
$commandName = Get-ProjectProperty $projectFile "VhAppCommandName";
$targetFramework = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.TargetFramework;
$publishDir = "$projectDir/bin/Publish-$distribution";
# the install root's two launchers, over current\<app>.dll, which only the setup installs
# (VhRootLaunchersDir in VpnHood.AppUi.Hosting.Desktop.Windows.targets)
$launchersDir = "$projectDir/bin/Publish-$distribution-root";

# module paths (dir keyed by the stable app folder; file names use the artifact title)
$moduleDir = "$packagesRootDir/$appFolder/windows-$distribution";
$moduleDirLatest = "$packagesRootDirLatest/$appFolder/windows-$distribution";
$module_infoFile = "$moduleDir/$packageFileTitle-win-x64.json";
$module_packageFile = [System.IO.Path]::ChangeExtension($module_infoFile, ".exe");
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

	# publish, with the install root's launchers beside it
	# NOTE: appSettings will not load from private files if p:SolutionDir=$solutionDir is not set
	try { Remove-Item -Path $launchersDir -Force -Recurse } catch {}
	dotnet publish $projectDir `
		/p:SolutionDir=$solutionDir `
		/p:Configuration=Release `
		/p:Version=$versionParam `
		"/p:VhRootLaunchersDir=$launchersDir" `
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
	Invoke-VhSign (Get-ChildItem $publishDir, $launchersDir -File -Recurse -Include "*.exe", "*.dll" |
		Select-Object -ExpandProperty FullName);

	# The apphosts are the files Windows shows in the UAC/SmartScreen prompt, so none may slip through
	# unsigned when signing is configured (a renamed AssemblyName would silently miss the glob above):
	# the window's exe and the console launcher beside it, in the version's folder and in the root.
	$appHostNames = @("$assemblyName.exe") + @(if ($commandName) { "$commandName.exe" });
	foreach ($appHostFile in @($appHostNames | ForEach-Object { "$publishDir/$_"; "$launchersDir/$_" })) {
		if (-not (Test-Path $appHostFile)) { Throw "The build did not make '$appHostFile'."; }
		if ($signEnabled -and -not (Get-AuthenticodeSignature $appHostFile).SignerCertificate) {
			Throw "Signing is configured but '$appHostFile' is still unsigned after the signing step.";
		}
	}
}

# =====================================================================================
# Stage: package — wrap the published binary into the NSIS setup (install-client.nsi)
# =====================================================================================
if ($doPackage) {
	Write-Host;
	Write-Host "*** [package] Building the setup for $packageFileTitle ..." -BackgroundColor Blue -ForegroundColor White;

	# makensis: on the PATH, else where the NSIS installer puts it (winget NSIS.NSIS, choco nsis)
	$makensisFile = (Get-Command "makensis.exe" -ErrorAction SilentlyContinue).Source;
	if (-not $makensisFile) {
		$makensisFile = @("${env:ProgramFiles(x86)}\NSIS\makensis.exe", "$env:ProgramFiles\NSIS\makensis.exe") |
			Where-Object { Test-Path $_ } | Select-Object -First 1;
	}
	if (-not $makensisFile) { Throw "Could not locate makensis.exe: install NSIS (winget install NSIS.NSIS)."; }

	# prepare module folders
	PrepareModuleFolder $moduleDir $moduleDirLatest;

	# the app's names and files, as its identity and its publish give them
	$company = Get-ProjectProperty $projectFile "Company";
	$copyright = Get-ProjectProperty $projectFile "Copyright";
	$iconFile = Join-Path $projectDir (Get-ProjectProperty $projectFile "ApplicationIcon");
	$version4 = (@($versionParam.Split('-')[0].Split('.')) + @("0", "0", "0"))[0..3] -join ".";
	$setupFile = "$moduleDir/$packageFileTitle-win-x64.exe";
	$nsisDefines = @(
		"/DPRODUCT_NAME=$productName",
		"/DINSTANCE_NAME=$assemblyName",
		"/DCOMMAND_NAME=$commandName",
		"/DVERSION=$versionParam",
		"/DVERSION4=$version4",
		"/DCOMPANY=$company",
		"/DCOPYRIGHT=$copyright",
		"/DICON=$((Resolve-Path $iconFile).Path)",
		"/DSOURCE_DIR=$((Resolve-Path $publishDir).Path)",
		"/DLAUNCHERS_DIR=$((Resolve-Path $launchersDir).Path)",
		"/DOUTPUT_FILE=$([IO.Path]::GetFullPath($setupFile))"
	) + @(if ($legacyMsiUpgradeCode) { "/DLEGACY_MSI_UPGRADE_CODE=$legacyMsiUpgradeCode" });
	# the uninstaller sits inside the setup, so makensis has it signed as it packs it
	if ($signEnabled) { $nsisDefines += "/DSIGN_SCRIPT=$PSScriptRoot\Sign-File.ps1"; }

	# Build Setup
	& $makensisFile /V2 /WX @nsisDefines "$PSScriptRoot/vh-installer/win/install-client.nsi";
	if ($LASTEXITCODE -ne 0) { Throw "makensis failed (exit $LASTEXITCODE)."; }

	# sign the built installer
	Invoke-VhSign @("$setupFile");

	# Verify the installer actually carries a signature and surface it explicitly. Signing is optional
	# (see $signEnabled) so an unsigned build is NOT fatal — but it must never pass silently: warn on the
	# command line (local builds) and as a GitHub annotation (CI). When signing is configured yet the
	# file is still unsigned, the signing step misbehaved, so the warning is worded differently.
	$sig = Get-AuthenticodeSignature $setupFile;
	if ($sig.SignerCertificate) {
		# A certificate is embedded -> the file IS signed. Status may be non-'Valid' on the build agent
		# (e.g. a short-lived Trusted Signing cert whose chain isn't validated locally); that is not an
		# "unsigned" condition, so just report it.
		$note = if ($sig.Status -eq 'Valid') { "Valid" } else { "signed (Authenticode status: $($sig.Status))" };
		Write-Host "Setup signature: $note ($($sig.SignerCertificate.Subject))." -ForegroundColor Green;
	}
	elseif (-not $signEnabled) {
		Write-VhBuildWarning ("The Windows installer '$module_packageFileName' is UNSIGNED: Azure Trusted Signing is not " +
			"configured. Set AZURE_SIGNING_CREDENTIAL and AZURE_SIGNING_TARGET (see docs/cicd/deployment.md) to sign the build.") `
			"Windows build is unsigned — Azure signing not configured";
	}
	else {
		Write-VhBuildWarning ("The Windows installer '$module_packageFileName' is UNSIGNED even though signing is " +
			"configured — the signing step produced no signature.") `
			"Windows installer failed to sign";
	}

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

	if ($isLatest)
	{
		Copy-Item -path "$moduleDir/*" -Destination "$moduleDirLatest/" -Force -Recurse;
	}
}

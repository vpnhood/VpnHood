# The head's MSIX: the app and its VPN host (../Client.Windows.VpnPlugin), each published
# self-contained for win-x64, staged with the C#/WinRT component's metadata and the manifest stamped,
# then packed and signed. A development certificate is made here and kept beside the package while it
# matches -publisher; _install.ps1 trusts it. The Store's own identity and signing are phase 2's.
param(
	[Parameter(Mandatory=$false)] [ValidateSet("Debug", "Release")] [String]$configuration = "Release",
	[Parameter(Mandatory=$false)] [String]$publisher = "CN=VpnHoodDevelopment",
	# Windows updates an installed package in place only to another version, and an uninstall first
	# can strand the VPN plug-in (the fork's lesson 14), so a development package's revision is its
	# build's minute; a Store package's is 0, the only one the Store takes.
	[Parameter(Mandatory=$false)] [int]$revision = [int](([DateTime]::UtcNow - [DateTime]::new(2026, 1, 1)).TotalMinutes % 65535)
)

$ErrorActionPreference = "Stop";
$projectDir = $PSScriptRoot;
$solutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path (Split-Path -Parent -Path $projectDir)));
$appProject = Join-Path $projectDir "VpnHood.App.Client.Windows.Microsoft.csproj";
$vpnHostProject = Join-Path $projectDir "../Client.Windows.VpnPlugin/VpnHood.App.Client.Windows.VpnPlugin.csproj";
$componentDir = Join-Path $solutionDir "src/Core/VpnHood.Core.Client.Devices.WindowsVpnPlugin.Activation";
$componentName = "VpnHood.Core.Client.Devices.WindowsVpnPlugin.Activation";

$outDir = Join-Path $projectDir "bin/Publish-microsoft";
$appDir = Join-Path $outDir "app";
$vpnHostDir = Join-Path $outDir "vpn-host";
$stageDir = Join-Path $outDir "stage";
$pfxPath = Join-Path $outDir "dev-certificate.pfx";
$cerPath = Join-Path $outDir "dev-certificate.cer";
$pfxPassword = "VpnHoodDevelopment"; # a throwaway certificate's, for sideloading only

# MSBuild answers in JSON only for two properties or more, so a second one rides along.
function Get-ProjectProperties([string]$project, [string[]]$names) {
	$json = dotnet msbuild $project -nologo "-p:Configuration=$configuration" (($names + "TargetFramework") | ForEach-Object { "-getProperty:$_" });
	if ($LASTEXITCODE -ne 0) { throw "Could not read the properties of $project." }
	return ($json | Out-String | ConvertFrom-Json).Properties;
}

# what the manifest names, from the projects themselves
$app = Get-ProjectProperties $appProject @("AssemblyName", "VhAppDisplayName", "Version");
$vpnHost = Get-ProjectProperties $vpnHostProject @("AssemblyName");
$numbers = [Version]($app.Version.Split('-')[0]);
$packageVersion = "$($numbers.Major).$($numbers.Minor).$($numbers.Build).$revision";
$packagePath = Join-Path $outDir "$($app.AssemblyName).msix";

# a clean stage every time, so no file of another build or configuration reaches the package
foreach ($dir in @($appDir, $vpnHostDir, $stageDir)) {
	Remove-Item $dir -Recurse -Force -ErrorAction Ignore;
}
New-Item -ItemType Directory -Path (Join-Path $stageDir "Assets") -Force | Out-Null;

# The component's metadata is the one file of it its consumers do not get: removed first, so the
# build below writes it again and the package never carries an older one.
$componentOutput = Join-Path $componentDir "bin/x64/$configuration";
Get-ChildItem -Path $componentOutput -Recurse -Filter "$componentName.winmd" -ErrorAction Ignore |
	Remove-Item -Force;

dotnet publish $appProject -c $configuration -o $appDir;
if ($LASTEXITCODE -ne 0) { throw "The app's publish failed with exit code $LASTEXITCODE." }
# x64, or CsWinRT gives the component its x86 WinRT.Host.dll, which Windows refuses in this package (0x80073CFC).
dotnet publish $vpnHostProject -c $configuration -p:Platform=x64 -o $vpnHostDir;
if ($LASTEXITCODE -ne 0) { throw "The VPN host's publish failed with exit code $LASTEXITCODE." }

$winmd = @(Get-ChildItem -Path $componentOutput -Recurse -Filter "$componentName.winmd");
if ($winmd.Count -ne 1) { throw "Expected one $componentName.winmd after the build, found $($winmd.Count)." }

# the app first, then the VPN host, whose WinRT.Host.runtimeconfig.json is the component's own
Copy-Item -Path (Join-Path $appDir "*") -Destination $stageDir -Recurse -Force;
Copy-Item -Path (Join-Path $vpnHostDir "*") -Destination $stageDir -Recurse -Force;
Copy-Item -LiteralPath $winmd[0].FullName -Destination $stageDir -Force;

# Without its runtime section, activation fails (0x80008093, the fork's lesson 3).
$runtimeConfig = Get-Content -Raw (Join-Path $stageDir "WinRT.Host.runtimeconfig.json") | ConvertFrom-Json;
if (-not $runtimeConfig.runtimeOptions) {
	throw "The staged WinRT.Host.runtimeconfig.json lacks the runtime options.";
}

# The two causes of a VPN host that never activates: an x86 WinRT.Host.dll, and the component loaded a second time
# beside the one the host's Main runs (the VPN host's CsWinRTLoadComponentsInDefaultALC).
$winrtHost = [IO.File]::ReadAllBytes((Join-Path $stageDir "WinRT.Host.dll"));
if ([BitConverter]::ToUInt16($winrtHost, [BitConverter]::ToInt32($winrtHost, 60) + 4) -ne 0x8664) {
	throw "The staged WinRT.Host.dll is not the x64 one.";
}
$vpnHostConfig = Get-Content -Raw (Join-Path $stageDir "$($vpnHost.AssemblyName).runtimeconfig.json") | ConvertFrom-Json;
if ($vpnHostConfig.runtimeOptions.configProperties.CSWINRT_LOAD_COMPONENTS_IN_DEFAULT_ALC -ne $true) {
	throw "The staged VPN host's runtimeconfig does not load components into the default context.";
}

# A valid minimal PNG: the package's pictures are phase 2's.
[IO.File]::WriteAllBytes((Join-Path $stageDir "Assets/Logo.png"),
	[Convert]::FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));

# the manifest, stamped
$manifestPath = Join-Path $stageDir "AppxManifest.xml";
[xml]$manifest = Get-Content -Raw (Join-Path $projectDir "Package.appxmanifest");
$manifest.Package.Identity.Publisher = $publisher;
$manifest.Package.Identity.Version = $packageVersion;
$manifest.Package.Properties.DisplayName = $app.VhAppDisplayName;
foreach ($application in $manifest.Package.Applications.Application) {
	if ($application.Id -eq "VpnPluginHost") {
		$application.Executable = "$($vpnHost.AssemblyName).exe";
		$application.VisualElements.DisplayName = "$($app.VhAppDisplayName) VPN host";
		# one server, the VPN host, for the VPN task and both app services; the keep-awake process is its own exe
		foreach ($extension in $application.Extensions.Extension) {
			foreach ($server in @($extension.BackgroundTasks, $extension.AppService)) {
				if ($server) { $server.ServerName = $vpnHost.AssemblyName; }
			}
			if ($extension.Category -eq "windows.fullTrustProcess") { $extension.Executable = "$($vpnHost.AssemblyName).exe"; }
		}
	}
	else {
		$application.Executable = "$($app.AssemblyName).exe";
		$application.VisualElements.DisplayName = $app.VhAppDisplayName;
	}
}
$manifest.Save($manifestPath);

# the newest Windows SDK that has the packing tools
$sdkBins = @(Get-ChildItem -LiteralPath "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Directory |
	Where-Object { $_.Name -match '^\d+(\.\d+){3}$' -and (Test-Path (Join-Path $_.FullName "x64/makeappx.exe")) } |
	Sort-Object { [Version]$_.Name } -Descending);
if ($sdkBins.Count -eq 0) { throw "No Windows SDK with MakeAppx.exe was found." }
$makeAppx = Join-Path $sdkBins[0].FullName "x64/makeappx.exe";
$signTool = Join-Path $sdkBins[0].FullName "x64/signtool.exe";

# Windows installs no package whose publisher differs from its signer's subject, so a kept
# certificate serves only while it names -publisher and has a day left.
$needCertificate = $true;
if (Test-Path -LiteralPath $pfxPath) {
	$kept = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($pfxPath, $pfxPassword);
	try {
		$needCertificate = $kept.Subject -ne $publisher -or $kept.NotAfter -le (Get-Date).AddDays(1);
	}
	finally {
		$kept.Dispose();
	}
}
if ($needCertificate) {
	Remove-Item -LiteralPath $pfxPath, $cerPath -Force -ErrorAction Ignore;
	$certificate = New-SelfSignedCertificate -Type Custom -Subject $publisher -KeyUsage DigitalSignature `
		-FriendlyName "VpnHood development package" -CertStoreLocation "Cert:\CurrentUser\My" `
		-TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3");
	Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password (ConvertTo-SecureString $pfxPassword -AsPlainText -Force) | Out-Null;
	Export-Certificate -Cert $certificate -FilePath $cerPath | Out-Null;

	# Windows makes a certificate only in a store, and copies it to the intermediate authorities too;
	# the .pfx signs, so neither copy stays.
	Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -DeleteKey;
	$caCopy = "Cert:\CurrentUser\CA\$($certificate.Thumbprint)";
	if (Test-Path -LiteralPath $caCopy) { Remove-Item -LiteralPath $caCopy; }
}

Remove-Item -LiteralPath $packagePath -Force -ErrorAction Ignore;
& $makeAppx pack /d $stageDir /p $packagePath /o;
if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed with exit code $LASTEXITCODE." }
& $signTool sign /fd SHA256 /f $pfxPath /p $pfxPassword $packagePath;
if ($LASTEXITCODE -ne 0) { throw "SignTool failed with exit code $LASTEXITCODE." }

Write-Host "Package: $packagePath ($packageVersion, $publisher)" -ForegroundColor Green;
Write-Host "Certificate: $cerPath" -ForegroundColor Green;

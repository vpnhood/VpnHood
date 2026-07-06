# Materializes the iOS App Store signing secrets so PublishIosApp.ps1 can produce a signed .ipa in CI.
# macOS ONLY (uses `security` + PlistBuddy). Run on the iOS build runner BEFORE Client.Ios/_publish.ps1.
#
# Secrets (each independent; ALL three of the first group are needed for real signing):
#   IOS_DISTRIBUTION_CERT_BASE64      base64 of the Apple Distribution certificate .p12
#   IOS_DISTRIBUTION_CERT_PASSWORD    its export password
#   IOS_PROVISION_APP_BASE64          base64 of the App Store provisioning profile for the app
#                                     (com.vpnhood.client.ios)
#   IOS_PROVISION_EXT_BASE64          base64 of the App Store provisioning profile for the Network
#                                     Extension (com.vpnhood.client.ios.networkextension)
#
# UNLIKE Android there is NO ephemeral fallback: an App Store .ipa cannot be self-signed. When any
# required secret is absent we DON'T fail — we write ios-signing.json { Signed: false } and warn. The
# publish step then does a codesign-disabled compile check (no .ipa) and the App Store upload job skips.
# Add the secrets to switch real signing on. Mirrors the gated design of PrepareCiAndroidSigning.ps1.
#
# Output: .user/VpnHoodClient/ios/ios-signing.json — { Signed, CodesignKey, AppProvision, ExtProvision }.

$ErrorActionPreference = "Stop";

$solutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot);
$userDir = Join-Path (Split-Path -Parent $solutionDir) ".user";
$appFolder = "VpnHoodClient";

$iosDir = Join-Path (Join-Path $userDir $appFolder) "ios";
New-Item -ItemType Directory -Path $iosDir -Force | Out-Null;
$markerFile = Join-Path $iosDir "ios-signing.json";

function Get-Env([string]$name) { [Environment]::GetEnvironmentVariable($name) }

$certB64 = Get-Env "IOS_DISTRIBUTION_CERT_BASE64";
$certPass = Get-Env "IOS_DISTRIBUTION_CERT_PASSWORD";
$appProfB64 = Get-Env "IOS_PROVISION_APP_BASE64";
$extProfB64 = Get-Env "IOS_PROVISION_EXT_BASE64";

function Write-Unsigned([string]$reason) {
	Write-Host "::warning title=iOS signing not configured::$reason The iOS build will be UNSIGNED (no .ipa) and the App Store upload will be skipped. Add the IOS_DISTRIBUTION_CERT_* + IOS_PROVISION_* secrets (see .github/DEPLOYMENT.md) to enable it.";
	@{ Signed = $false } | ConvertTo-Json | Out-File $markerFile -Encoding ASCII;
}

if (-not ($certB64 -and $certPass -and $appProfB64 -and $extProfB64)) {
	Write-Unsigned "One or more iOS signing secrets are missing (need cert + password + app profile + extension profile).";
	return;
}

if (-not $IsMacOS) {
	Write-Unsigned "iOS signing can only be materialized on macOS (this runner is not macOS).";
	return;
}

# --- import the distribution certificate into a dedicated CI keychain ---
$keychain = Join-Path $iosDir "vpnhood-ci.keychain-db";
$keychainPass = "ci-$([guid]::NewGuid().ToString('N'))";
$certFile = Join-Path $iosDir "distribution.p12";
[IO.File]::WriteAllBytes($certFile, [Convert]::FromBase64String($certB64));

# Recreate the keychain fresh each run and add it to the search list (so codesign can find the identity).
& security delete-keychain "$keychain" 2>$null | Out-Null;
& security create-keychain -p "$keychainPass" "$keychain";
& security set-keychain-settings -lut 21600 "$keychain";
& security unlock-keychain -p "$keychainPass" "$keychain";
& security import "$certFile" -k "$keychain" -P "$certPass" -T /usr/bin/codesign -T /usr/bin/security;
# Allow codesign to use the key without an interactive prompt.
& security set-key-partition-list -S apple-tool:,apple: -s -k "$keychainPass" "$keychain" 2>$null | Out-Null;
$loginKeychain = (& security default-keychain | ForEach-Object { $_.Trim().Trim('"') });
& security list-keychains -d user -s "$keychain" "$loginKeychain";
Remove-Item $certFile -Force -ErrorAction SilentlyContinue;

# Resolve the codesign identity common name (e.g. "Apple Distribution: OmegaHood LLC (6KKW3MKLR7)").
$identity = $null;
foreach ($line in (& security find-identity -v -p codesigning "$keychain")) {
	if ($line -match '"(Apple Distribution:[^"]+)"' -or $line -match '"(iPhone Distribution:[^"]+)"') { $identity = $Matches[1]; break; }
}
if (-not $identity) { Write-Unsigned "The provided certificate is not an Apple/iPhone Distribution identity (find-identity found none)."; return; }

# --- install both provisioning profiles into ~/Library/MobileDevice/Provisioning Profiles/ ---
$profilesDir = Join-Path $HOME "Library/MobileDevice/Provisioning Profiles";
New-Item -ItemType Directory -Path $profilesDir -Force | Out-Null;

function Install-Profile([string]$b64, [string]$label) {
	$tmp = Join-Path $iosDir "$label.mobileprovision";
	[IO.File]::WriteAllBytes($tmp, [Convert]::FromBase64String($b64));
	$plist = Join-Path $iosDir "$label.plist";
	& security cms -D -i "$tmp" -o "$plist";
	$uuid = (& /usr/libexec/PlistBuddy -c "Print :UUID" "$plist").Trim();
	$name = (& /usr/libexec/PlistBuddy -c "Print :Name" "$plist").Trim();
	Copy-Item -Path $tmp -Destination (Join-Path $profilesDir "$uuid.mobileprovision") -Force;
	Remove-Item $tmp, $plist -Force -ErrorAction SilentlyContinue;
	return $name;
}

$appProvName = Install-Profile $appProfB64 "app";
$extProvName = Install-Profile $extProfB64 "ext";

@{
	Signed = $true;
	CodesignKey = $identity;
	AppProvision = $appProvName;
	ExtProvision = $extProvName;
} | ConvertTo-Json | Out-File $markerFile -Encoding ASCII;

Write-Host "iOS signing ready: identity='$identity' appProfile='$appProvName' extProfile='$extProvName'" -ForegroundColor Cyan;

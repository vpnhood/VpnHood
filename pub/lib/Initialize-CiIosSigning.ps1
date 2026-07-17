# Materializes the iOS App Store signing secrets so Publish-IosApp.ps1 can produce a signed .ipa in CI.
# macOS ONLY (uses `security` + PlistBuddy). Run on the iOS build runner BEFORE Client.Ios/_publish.ps1.
#
# Secrets (each independent; ALL three of the first group are needed for real signing):
#   APPLE_DISTRIBUTION_CERT_BASE64      base64 of the Apple Distribution certificate .p12
#   APPLE_DISTRIBUTION_CERT_PASSWORD    its export password
#   IOS_PROVISION_APP_BASE64          base64 of the App Store provisioning profile for the app
#                                     (com.vpnhood.client.ios)
#   IOS_PROVISION_EXT_BASE64          base64 of the App Store provisioning profile for the Network
#                                     Extension (com.vpnhood.client.ios.networkextension)
#
# UNLIKE Android there is NO ephemeral fallback: an App Store .ipa cannot be self-signed. When any
# required secret is absent we DON'T fail — we write ios_signing.json { Signed: false } and warn. The
# publish step then does a codesign-disabled compile check (no .ipa) and the App Store upload job skips.
# Add the secrets to switch real signing on. Mirrors the gated design of Initialize-CiAndroidSigning.ps1.
#
# Output: .user/<appFolder>/ios/ios_signing.json — { Signed, CodesignKey, AppProvision, ExtProvision }.

param(
	# The .user/<appFolder>/ios config folder to materialize signing into. Defaults to VpnHoodClient so
	# the Client CI call site stays unchanged; the Connect CI passes -appFolder VpnHoodConnect.
	[string]$appFolder = "VpnHoodClient"
)

$ErrorActionPreference = "Stop";

$solutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot);
$userDir = Join-Path (Split-Path -Parent $solutionDir) ".user";

$iosDir = Join-Path (Join-Path $userDir $appFolder) "ios";
New-Item -ItemType Directory -Path $iosDir -Force | Out-Null;
$markerFile = Join-Path $iosDir "ios_signing.json";

function Get-Env([string]$name) { [Environment]::GetEnvironmentVariable($name) }

$certB64 = Get-Env "APPLE_DISTRIBUTION_CERT_BASE64";
$certPass = Get-Env "APPLE_DISTRIBUTION_CERT_PASSWORD";
$appProfB64 = Get-Env "IOS_PROVISION_APP_BASE64";
$extProfB64 = Get-Env "IOS_PROVISION_EXT_BASE64";

function Write-Unsigned([string]$reason) {
	Write-Host "::warning title=iOS signing not configured::$reason The iOS build will be UNSIGNED (no .ipa) and the App Store upload will be skipped. Add the APPLE_DISTRIBUTION_CERT_* + IOS_PROVISION_* secrets (see .github/DEPLOYMENT.md) to enable it.";
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

# --- normalize the .p12 to a macOS-importable (legacy) PKCS#12 encoding ---
# A .p12 exported by OpenSSL 3 protects its MAC with PBKDF2/AES-256+SHA-256, which macOS `security`
# (SecPKCS12Import) cannot read -> "SecKeychainItemImport: MAC verification failed" even when
# APPLE_DISTRIBUTION_CERT_PASSWORD is CORRECT (this is what silently shipped unsigned builds with no
# .ipa). Re-encode with the legacy SHA1-MAC / 3DES scheme `security` accepts: decrypt to PEM, then
# re-export. Try `-legacy` first (OpenSSL 3 needs it to emit legacy algorithms); fall back to no flag
# (LibreSSL, the macOS system openssl, rejects `-legacy` and already defaults to the legacy scheme).
# Best-effort: on any openssl failure leave $certFile untouched and let the import + its loud throw run.
$pem = Join-Path $iosDir "distribution.pem";
$legacyP12 = Join-Path $iosDir "distribution.legacy.p12";
& openssl pkcs12 -in "$certFile" -passin "pass:$certPass" -nodes -out "$pem" 2>$null;
if ($LASTEXITCODE -eq 0) {
	& openssl pkcs12 -export -legacy -in "$pem" -passout "pass:$certPass" -out "$legacyP12" 2>$null;
	if ($LASTEXITCODE -ne 0) { & openssl pkcs12 -export -in "$pem" -passout "pass:$certPass" -out "$legacyP12" 2>$null; }
	if ($LASTEXITCODE -eq 0 -and (Test-Path $legacyP12)) {
		Move-Item -Force $legacyP12 $certFile;
		Write-Host "Re-encoded the distribution .p12 to the legacy PKCS#12 scheme for macOS `security` import." -ForegroundColor Cyan;
	}
}
Remove-Item $pem, $legacyP12 -Force -ErrorAction SilentlyContinue;

# Recreate the keychain fresh each run and add it to the search list (so codesign can find the identity).
& security delete-keychain "$keychain" 2>$null | Out-Null;
& security create-keychain -p "$keychainPass" "$keychain";
& security set-keychain-settings -lut 21600 "$keychain";
& security unlock-keychain -p "$keychainPass" "$keychain";
& security import "$certFile" -k "$keychain" -P "$certPass" -T /usr/bin/codesign -T /usr/bin/security;
# The signing secrets ARE present (we passed the gate above), so a failed import is a HARD ERROR — NOT
# an unsigned-fork fallback. Silently going unsigned here is what let a broken cert/password ship a
# green build with no .ipa. 'MAC verification failed' == APPLE_DISTRIBUTION_CERT_PASSWORD does not
# match the .p12 in APPLE_DISTRIBUTION_CERT_BASE64.
if ($LASTEXITCODE -ne 0) {
	Remove-Item $certFile -Force -ErrorAction SilentlyContinue;
	& security delete-keychain "$keychain" 2>$null | Out-Null;
	throw "iOS signing FAILED: could not import the Distribution certificate. The signing secrets are set, so this is a real error (not a fork's unsigned fallback). Most likely APPLE_DISTRIBUTION_CERT_PASSWORD does not match APPLE_DISTRIBUTION_CERT_BASE64 (security reported 'MAC verification failed'), or the .p12 is corrupt. Fix the cert/password pair and re-run.";
}
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
if (-not $identity) {
	& security delete-keychain "$keychain" 2>$null | Out-Null;
	# Secrets present + import succeeded but no codesigning identity => wrong cert TYPE (e.g. a
	# Development cert, or the private key is missing from the .p12). Hard error, not a silent skip.
	throw "iOS signing FAILED: the imported certificate is not an Apple/iPhone Distribution codesigning identity (find-identity found none). Ensure APPLE_DISTRIBUTION_CERT_BASE64 is an *Apple Distribution* .p12 that INCLUDES its private key.";
}

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

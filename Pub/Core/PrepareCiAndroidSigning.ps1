# Prepares ../.user (credentials.json + keystores) so the EXISTING PublishAndroidApp.ps1
# can run unchanged in CI. PublishAndroidApp reads, per build:
#   $credentials."Android.VpnHoodClient.<dist>".{KeyStoreFile,KeyStorePass,KeyStoreAlias}
# and resolves the keystore at "$solutionDir/../.user/<KeyStoreFile>".
#
# Two modes, chosen automatically from the environment (mirrors the gated Windows signing):
#
#   * REAL signing — used for production releases. Provide the keystores + secrets:
#       ANDROID_KEYSTORE_WEB_BASE64 / _PASS / _ALIAS      (Web + arm64-web APKs)
#       ANDROID_KEYSTORE_GOOGLE_BASE64 / _PASS / _ALIAS   (Google AAB)
#     Both groups must be present, or the script falls back to ephemeral and warns.
#
#   * EPHEMERAL signing — the default when no keystore secrets are set. Generates a
#     throwaway keystore with keytool so the pipeline builds end-to-end WITHOUT exposing
#     the real app-signing key (important: never upload the production Web release key to
#     a public/test repo). Artifacts signed this way are NOT release/Play-Store grade.
#
# Run AFTER a JDK is on PATH (keytool) and BEFORE the Android _publish.ps1 scripts.

$ErrorActionPreference = "Stop";

$solutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot);
$userDir = Join-Path (Split-Path -Parent $solutionDir) ".user";
New-Item -ItemType Directory -Path $userDir -Force | Out-Null;

# Start from any existing credentials.json (keeps the NuGet stub) or a fresh object.
$credsFile = Join-Path $userDir "credentials.json";
if (Test-Path $credsFile) {
    $creds = Get-Content $credsFile -Raw | ConvertFrom-Json;
} else {
    $creds = [pscustomobject]@{ NugetApiKey = "" };
}

function Set-CredNode($obj, [string]$name, [hashtable]$value) {
    $node = [pscustomobject]$value;
    if ($obj.PSObject.Properties.Name -contains $name) { $obj.$name = $node; }
    else { $obj | Add-Member -NotePropertyName $name -NotePropertyValue $node; }
}

$webSecretsSet = $env:ANDROID_KEYSTORE_WEB_BASE64 -and $env:ANDROID_KEYSTORE_WEB_PASS -and $env:ANDROID_KEYSTORE_WEB_ALIAS;
$googleSecretsSet = $env:ANDROID_KEYSTORE_GOOGLE_BASE64 -and $env:ANDROID_KEYSTORE_GOOGLE_PASS -and $env:ANDROID_KEYSTORE_GOOGLE_ALIAS;
$useReal = $webSecretsSet -and $googleSecretsSet;

if (($webSecretsSet -or $googleSecretsSet) -and -not $useReal) {
    Write-Host "::warning::Partial Android keystore secrets provided; need BOTH the WEB and GOOGLE groups. Falling back to an ephemeral keystore.";
}

if ($useReal) {
    Write-Host "Android signing: using REAL keystores from secrets." -ForegroundColor Cyan;

    $webFile = "android-web-release.keystore";
    [IO.File]::WriteAllBytes((Join-Path $userDir $webFile), [Convert]::FromBase64String($env:ANDROID_KEYSTORE_WEB_BASE64));
    $googleFile = "android-google-release.keystore";
    [IO.File]::WriteAllBytes((Join-Path $userDir $googleFile), [Convert]::FromBase64String($env:ANDROID_KEYSTORE_GOOGLE_BASE64));

    $webNode = @{ KeyStoreFile = $webFile; KeyStorePass = $env:ANDROID_KEYSTORE_WEB_PASS; KeyStoreAlias = $env:ANDROID_KEYSTORE_WEB_ALIAS };
    $googleNode = @{ KeyStoreFile = $googleFile; KeyStorePass = $env:ANDROID_KEYSTORE_GOOGLE_PASS; KeyStoreAlias = $env:ANDROID_KEYSTORE_GOOGLE_ALIAS };
}
else {
    Write-Host "Android signing: generating an EPHEMERAL keystore (no keystore secrets set; NOT release-grade)." -ForegroundColor Yellow;

    $ksFile = "ci-ephemeral.keystore";
    $ksPath = Join-Path $userDir $ksFile;
    $ksPass = "ciephemeral";   # throwaway; this keystore is regenerated every run
    $ksAlias = "ci";
    if (Test-Path $ksPath) { Remove-Item $ksPath -Force; }

    keytool -genkeypair -v -keystore "$ksPath" -alias $ksAlias `
        -keyalg RSA -keysize 2048 -validity 10000 `
        -storepass $ksPass -keypass $ksPass `
        -dname "CN=VpnHood CI Ephemeral, O=OmegaHood LLC, C=US";
    if ($LASTEXITCODE -ne 0) { Throw "keytool failed to generate the ephemeral keystore (exit $LASTEXITCODE)."; }

    $webNode = @{ KeyStoreFile = $ksFile; KeyStorePass = $ksPass; KeyStoreAlias = $ksAlias };
    $googleNode = $webNode;
}

# Web + arm64-web share one keystore; Google has its own.
Set-CredNode $creds "Android.VpnHoodClient.Web" $webNode;
Set-CredNode $creds "Android.VpnHoodClient.arm64-web" $webNode;
Set-CredNode $creds "Android.VpnHoodClient.Google" $googleNode;

$creds | ConvertTo-Json -Depth 10 | Out-File -FilePath $credsFile -Encoding ascii -Force;
Write-Host "Wrote Android signing config to $credsFile";

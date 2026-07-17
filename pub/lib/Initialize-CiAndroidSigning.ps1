# Materializes the per-key Android signing secrets into ../.user so Publish-AndroidApp.ps1 runs
# unchanged in CI. Publish-AndroidApp reads per-store files under the app's .user folder:
#     .user/<app>/<store>/android_keystore_<store>.p12   and   .../android_keystore_<store>_password.txt
#     (+ optional .user/<app>/<store>/android_keystore_<store>_alias.txt)
# This script writes those files for every key. Only the keystore bytes and password are secret; the
# alias is auto-detected from the keystore by Publish-AndroidApp.ps1 (or read from publish.json /
# the optional _ALIAS secret). Ephemeral keystores below get a fixed alias that detection picks up.
#
# Which logical key writes into which .user app + store is read from android-signing.json (this dir).
#
# Each key is decided INDEPENDENTLY (mirrors the gated Windows signing):
#
#   * REAL signing — when both secrets are present for that key:
#       ANDROID_KEYSTORE_CLIENT_GOOGLE_BASE64  / _PASSWORD
#       ANDROID_KEYSTORE_CLIENT_WEB_BASE64     / _PASSWORD
#       ANDROID_KEYSTORE_CONNECT_GOOGLE_BASE64 / _PASSWORD
#       ANDROID_KEYSTORE_CONNECT_WEB_BASE64    / _PASSWORD
#     An optional ANDROID_KEYSTORE_<NAME>_ALIAS picks the key in a MULTI-entry keystore; for the
#     usual single-key keystore it's unneeded (the alias is auto-detected).
#
#   * EPHEMERAL signing — when a key's secrets are absent. Generates a throwaway PKCS12 keystore
#     (keytool) so the pipeline builds end-to-end WITHOUT the real key (never upload a production
#     key to a public/test repo). NOT release/Play-Store grade.
#
# Deciding per key means a workflow that only signs some apps (e.g. client) gets real signing for
# those without needing every app's secret.
#
# Non-android secrets (NUGET_API_KEY -> .user/nuget_api_key.txt, IP2LOCATION_TOKEN -> .user/ip2location_token.txt)
# are materialized by their own workflow steps, not here.
#
# Run AFTER a JDK is on PATH (keytool) and BEFORE the Android _publish.ps1 scripts.

$ErrorActionPreference = "Stop";

$solutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot);
$userDir = Join-Path (Split-Path -Parent $solutionDir) ".user";

# Fixed alias for generated ephemeral keystores; Publish-AndroidApp.ps1 auto-detects it.
$ephemeralAlias = "vpnhood";

# logical key (GitHub-secret prefix) -> target .user app + store, loaded from the signing map.
# Each store is its own secret (Connect's google/web are separate keys here even though the same
# physical key may back both). The alias is NOT in this map — it's auto-detected from the keystore
# (or the optional _ALIAS secret / publish.json).
$mapFile = Join-Path $PSScriptRoot "android-signing.json";
$keys = (Get-Content $mapFile -Raw | ConvertFrom-Json).keys |
    ForEach-Object { @{ Name = $_.name; App = $_.app; Store = $_.store } };

function Get-Env([string]$name) { [Environment]::GetEnvironmentVariable($name) }

# Returns the single PrivateKeyEntry alias in a keystore, or $null when there isn't exactly one
# (only a key entry can sign). Same logic Publish-AndroidApp.ps1 uses for auto-detection.
function Get-SingleKeyAlias([string]$ks, [string]$pass) {
    $aliases = @(); $current = $null;
    foreach ($line in (& keytool -list -v -keystore $ks -storepass $pass 2>&1)) {
        $s = $line.ToString();
        if ($s -match '^Alias name:\s*(.+?)\s*$') { $current = $Matches[1] }
        elseif ($s -match '^Entry type:\s*PrivateKeyEntry' -and $current) { $aliases += $current; $current = $null }
    }
    if ($aliases.Count -eq 1) { return $aliases[0] } else { return $null }
}

$realKeys = @(); $ephemeralKeys = @();
foreach ($k in $keys) {
    $b64 = Get-Env "ANDROID_KEYSTORE_$($k.Name)_BASE64";
    $pw = Get-Env "ANDROID_KEYSTORE_$($k.Name)_PASSWORD";
    # optional: only needed for a multi-entry keystore where auto-detect can't pick one key.
    $aliasOverride = Get-Env "ANDROID_KEYSTORE_$($k.Name)_ALIAS";
    if ($b64 -and -not $pw) {
        Write-Host "::warning::ANDROID_KEYSTORE_$($k.Name)_BASE64 is set but _PASSWORD is missing; using an ephemeral keystore for $($k.Name).";
    }

    $real = [bool]$b64 -and [bool]$pw;

    $dir = Join-Path (Join-Path $userDir $k.App) $k.Store;
    New-Item -ItemType Directory -Path $dir -Force | Out-Null;
    $ks = Join-Path $dir "android_keystore_$($k.Store).p12";
    $passFile = Join-Path $dir "android_keystore_$($k.Store)_password.txt";
    $aliasFile = Join-Path $dir "android_keystore_$($k.Store)_alias.txt";

    if ($real) {
        [IO.File]::WriteAllBytes($ks, [Convert]::FromBase64String($b64));
        $pass = $pw;
        $realKeys += $k.Name;
    }
    else {
        if (Test-Path $ks) { Remove-Item $ks -Force; }
        $pass = "ciephemeral";   # throwaway; regenerated every run
        keytool -genkeypair -v -keystore "$ks" -storetype PKCS12 -alias $ephemeralAlias `
            -keyalg RSA -keysize 2048 -validity 10000 `
            -storepass $pass -keypass $pass `
            -dname "CN=VpnHood CI Ephemeral, O=OmegaHood LLC, C=US";
        if ($LASTEXITCODE -ne 0) { Throw "keytool failed to generate ephemeral keystore for $($k.Name) (exit $LASTEXITCODE)."; }
        $ephemeralKeys += $k.Name;
    }

    [IO.File]::WriteAllText($passFile, $pass);

    # alias sidecar (non-secret): ephemeral uses the fixed alias; real uses ANDROID_KEYSTORE_<NAME>_ALIAS
    # when supplied (required for a multi-entry keystore), else auto-detects the single key entry.
    # Left unwritten if a real keystore isn't single-key and no override — Publish-AndroidApp then throws.
    $alias =
        if (-not $real) { $ephemeralAlias }
        elseif (-not [string]::IsNullOrWhiteSpace($aliasOverride)) { $aliasOverride.Trim() }
        else { Get-SingleKeyAlias $ks $pass };
    if ($alias) { [IO.File]::WriteAllText($aliasFile, $alias); }
}

Write-Host "Android signing under ${userDir}: REAL=[$($realKeys -join ', ')] EPHEMERAL=[$($ephemeralKeys -join ', ')]" -ForegroundColor Cyan;
if ($ephemeralKeys.Count -gt 0) { Write-Host "  (ephemeral keystores are NOT release/Play-Store grade)"; }

# Materializes the per-key Android signing secrets into ../.user so PublishAndroidApp.ps1 runs
# unchanged in CI. PublishAndroidApp derives the keystore folder from <packageFileTitle>/<store>
# and reads, per build:
#     .user/<dir>/keystore.p12   and   .user/<dir>/keystore_pass.txt   (+ keystore_alias.txt)
# This script writes those two files for every key. Only the keystore bytes and password are
# secret; the alias is auto-detected from the keystore by PublishAndroidApp.ps1 (ephemeral
# keystores below are generated with a fixed alias that detection then picks up).
#
# Which logical key writes into which .user folder(s) is read from android-signing.json (this dir).
# That map holds NO alias — the alias is never sourced from it.
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
# Non-android secrets (NUGET_APIKEY -> .user/nuget_apikey.txt, IP2LOCATION_TOKEN -> .user/ip2location_token.txt)
# are materialized by their own workflow steps, not here.
#
# Run AFTER a JDK is on PATH (keytool) and BEFORE the Android _publish.ps1 scripts.

$ErrorActionPreference = "Stop";

$solutionDir = Split-Path -Parent -Path (Split-Path -Parent -Path $PSScriptRoot);
$userDir = Join-Path (Split-Path -Parent $solutionDir) ".user";

# Fixed alias for generated ephemeral keystores; PublishAndroidApp.ps1 auto-detects it.
$ephemeralAlias = "vpnhood";

# logical key (GitHub-secret prefix) -> target .user dir(s), loaded from the signing map.
# Each store folder is its own secret (Connect's google/web are separate keys here even though the
# same physical key may back both). The alias is NOT in this map — it's auto-detected from the
# keystore (or the optional _ALIAS secret).
$mapFile = Join-Path $PSScriptRoot "android-signing.json";
$keys = (Get-Content $mapFile -Raw | ConvertFrom-Json).keys |
    ForEach-Object { @{ Name = $_.name; Dirs = @($_.dirs) } };

function Get-Env([string]$name) { [Environment]::GetEnvironmentVariable($name) }

# Returns the single PrivateKeyEntry alias in a keystore, or $null when there isn't exactly one
# (only a key entry can sign). Same logic PublishAndroidApp.ps1 uses for auto-detection.
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
    if ($real) {
        $bytes = [Convert]::FromBase64String($b64);
        $pass = $pw;
        $realKeys += $k.Name;
    }
    else {
        $pass = "ciephemeral";   # throwaway; regenerated every run
        $ephemeralKeys += $k.Name;
    }

    $firstKs = $null;
    foreach ($d in $k.Dirs) {
        $dir = Join-Path $userDir $d;
        New-Item -ItemType Directory -Path $dir -Force | Out-Null;
        $ks = Join-Path $dir "keystore.p12";

        if ($real) {
            [IO.File]::WriteAllBytes($ks, $bytes);
        }
        elseif ($null -eq $firstKs) {
            if (Test-Path $ks) { Remove-Item $ks -Force; }
            keytool -genkeypair -v -keystore "$ks" -storetype PKCS12 -alias $ephemeralAlias `
                -keyalg RSA -keysize 2048 -validity 10000 `
                -storepass $pass -keypass $pass `
                -dname "CN=VpnHood CI Ephemeral, O=OmegaHood LLC, C=US";
            if ($LASTEXITCODE -ne 0) { Throw "keytool failed to generate ephemeral keystore for $($k.Name) (exit $LASTEXITCODE)."; }
            $firstKs = $ks;
        }
        else {
            Copy-Item $firstKs $ks -Force;   # same throwaway key into the other store folder
        }

        [IO.File]::WriteAllText((Join-Path $dir "keystore_pass.txt"), $pass);

        # alias sidecar (non-secret): ephemeral uses the fixed alias; real uses ANDROID_KEYSTORE_<NAME>_ALIAS
        # when supplied (required for a multi-entry keystore), else auto-detects the single key entry.
        # Left unwritten if a real keystore isn't single-key and no override — PublishAndroidApp then throws.
        $alias =
            if (-not $real) { $ephemeralAlias }
            elseif (-not [string]::IsNullOrWhiteSpace($aliasOverride)) { $aliasOverride.Trim() }
            else { Get-SingleKeyAlias $ks $pass };
        if ($alias) { [IO.File]::WriteAllText((Join-Path $dir "keystore_alias.txt"), $alias); }
    }
}

Write-Host "Android signing under ${userDir}: REAL=[$($realKeys -join ', ')] EPHEMERAL=[$($ephemeralKeys -join ', ')]" -ForegroundColor Cyan;
if ($ephemeralKeys.Count -gt 0) { Write-Host "  (ephemeral keystores are NOT release/Play-Store grade)"; }

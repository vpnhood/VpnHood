# Loads the optional per-app publish config from a single JSON file: .user/<appFolder>/publish.json.
# These are NON-secret build settings, so one file (mirrored by one GitHub *variable*) is far easier to
# manage than a folder of tiny files. Anyone who forks the repo can build their OWN app without editing a
# committed file. The app's runtime appsettings is a single SHARED file at the app root
# (.user/<app>/appsettings.json), embedded across all distributions. Signing keys/passwords/access keys
# stay as their own files in per-store subfolders (.user/<app>/<store>/) because each is its own GitHub
# secret — see Publish-AndroidApp.ps1 / Initialize-CiAndroidSigning.ps1.
#
# ALL-OR-NOTHING (strict) by design — the presence of publish.json is the switch:
#   * publish.json ABSENT  -> lenient: the build uses the committed built-in defaults (csproj
#                             <ApplicationId> etc.). This is the local/dev quick-build path.
#   * publish.json PRESENT  -> STRICT: every required key must be there or we THROW. There is NO
#                             field-level fallback to defaults, so a half-filled config can never
#                             silently ship the wrong id/repo. Same on GitHub: if the variable exists,
#                             the build refuses to run half-configured.
#
# publish.json shape (Android ids are Android-only — Windows/Linux have no package id):
#   {
#     "RepoUrl": "https://github.com/owner/repo",        REQUIRED  release repo for this app
#     "PackageTitle": "VpnHoodClient",                   REQUIRED  artifact title (renames output only)
#     "InstallationPageUrl": "https://.../download",     REQUIRED  Windows install/download page
#     "Distributions": {                                 optional per store (google = Play AAB, web = APKs)
#       "Google": { "AndroidPackageId": "...", "AndroidKeystoreAlias": "" },
#       "Web":    { "AndroidPackageId": "...", "AndroidKeystoreAlias": "" }
#     }
#   }
#
# A Distributions.<store> block is OPTIONAL (you needn't ship every store), but any block that IS present
# MUST name AndroidPackageId (the built /p:ApplicationId). AndroidKeystoreAlias stays optional: it's the
# signing alias, auto-detected from the keystore when omitted (that's derivation from the real key, not a
# guessed default). The title override renames published artifacts only; .user lookups + the bin module
# dir stay keyed by the default app folder, and Linux artifact names come from the csproj AssemblyName.

function Get-AppPublishConfig {
    param([Parameter(Mandatory = $true)][string]$appFolder)

    $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot);
    $jsonPath = Join-Path (Join-Path "$repoRoot/../.user" $appFolder) "publish.json";

    # Same shape regardless of whether the file exists, so callers never null-check the container.
    # `exists` is the strict-mode switch (see header): callers use it to decide default vs throw.
    $result = @{ exists = $false; repoUrl = $null; packageFileTitle = $null; installationPageUrl = $null;
                 packageId = @{}; keystoreAlias = @{} };
    if (-not (Test-Path $jsonPath)) { return $result; }
    $result.exists = $true;

    try { $json = Get-Content $jsonPath -Raw | ConvertFrom-Json; }
    catch { Throw "publish.json is not valid JSON ($jsonPath): $($_.Exception.Message)"; }

    # STRICT top-level: all three are required once publish.json exists (shared by win/linux/android).
    foreach ($k in @('RepoUrl', 'PackageTitle', 'InstallationPageUrl')) {
        if ([string]::IsNullOrWhiteSpace($json.$k)) {
            Throw "publish.json is present ($jsonPath) but '$k' is missing/empty. In strict mode every key is required (no fallback to defaults). Add '$k', or remove publish.json to build with built-in defaults.";
        }
    }
    $result.repoUrl             = $json.RepoUrl.Trim();
    $result.packageFileTitle    = $json.PackageTitle.Trim();
    $result.installationPageUrl = $json.InstallationPageUrl.Trim();

    # Distributions keyed by store (google | web); member access is case-insensitive. Any block that is
    # present must name AndroidPackageId; AndroidKeystoreAlias stays optional (auto-detected otherwise).
    foreach ($store in @('google', 'web')) {
        $dist = $json.Distributions.$store;
        if ($null -eq $dist) { continue; }
        if ([string]::IsNullOrWhiteSpace($dist.AndroidPackageId)) {
            Throw "publish.json ($jsonPath) declares Distributions.$store but 'AndroidPackageId' is missing/empty. Add it — strict mode has no fallback.";
        }
        $result.packageId[$store] = $dist.AndroidPackageId.Trim();
        if (-not [string]::IsNullOrWhiteSpace($dist.AndroidKeystoreAlias)) { $result.keystoreAlias[$store] = $dist.AndroidKeystoreAlias.Trim(); }
    }
    return $result;
}

# Strict guard for the app's SHARED appsettings (.user/<app>/appsettings.json, embedded as AppSettings.json
# by every distribution's csproj via Condition="Exists"). One file per app is shared across all stores /
# platforms (google, web, windows, linux, and future iOS): it's a superset — each distribution reads the
# keys it needs and ignores the rest. Enforced ONLY when publish.json exists (strict mode): with NO
# publish.json we build from built-in defaults and deliberately do NOT look for this — or any other —
# .user / GitHub config. Throws when strict and the file is absent, so a real build can never silently
# ship default settings via the csproj's Exists() short-circuit.
function Assert-AppSettings {
    param([Parameter(Mandatory = $true)][string]$appFolder)

    if (-not (Get-AppPublishConfig $appFolder).exists) { return; }   # no publish.json -> defaults only, no lookups
    $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot);
    $file = Join-Path (Join-Path "$repoRoot/../.user" $appFolder) "appsettings.json";
    if (-not (Test-Path $file)) {
        Throw "publish.json is present (strict mode) but the shared appsettings is missing: '$file'. It is embedded as AppSettings.json; a build without it would silently ship default settings. Add it, or remove publish.json to build with built-in defaults.";
    }
}

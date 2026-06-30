# Loads the optional per-app publish config from a single JSON file: .user/<appFolder>/publish.json.
# These are NON-secret build settings, so one file (mirrored by one GitHub *variable*) is far easier to
# manage than a folder of tiny files. Anyone who forks the repo can build their OWN app without editing a
# committed file. Secrets/binaries stay as their own files in per-store subfolders (.user/<app>/<store>/:
# keystores, passwords, appsettings, access keys) because each must be its own GitHub secret — see PublishAndroidApp.ps1 /
# PrepareCiAndroidSigning.ps1.
#
# publish.json shape (every field optional; absent file or field = project default):
#   {
#     "RepoUrl": "https://github.com/owner/repo",        release repo for this app (per app)
#     "PackageTitle": "VpnHoodClient",                   artifact title override (renames output only)
#     "InstallationPageUrl": "https://.../download",     Windows install/download page
#     "Distributions": {
#       "Google": { "PackageId": "...", "KeystoreAlias": "" },   per store (google = Play AAB)
#       "Web":    { "PackageId": "...", "KeystoreAlias": "" }    per store (web = web + arm64 APKs)
#     }
#   }
#
# PackageId is the built application id (/p:ApplicationId); Windows/Linux builds have no packageId. The
# KeystoreAlias is the signing alias — non-secret, so it lives here; absent = auto-detect the single key
# entry. The title override renames published artifacts only; .user lookups + the bin module dir stay
# keyed by the default app folder, and Linux artifact names come from the csproj AssemblyName.

function Get-AppPublishConfig {
    param([Parameter(Mandatory = $true)][string]$appFolder)

    $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot);
    $jsonPath = Join-Path (Join-Path "$repoRoot/../.user" $appFolder) "publish.json";

    # Same shape regardless of whether the file exists, so callers never null-check the container.
    $result = @{ repoUrl = $null; packageFileTitle = $null; installationPageUrl = $null;
                 packageId = @{}; keystoreAlias = @{} };
    if (-not (Test-Path $jsonPath)) { return $result; }

    $json = Get-Content $jsonPath -Raw | ConvertFrom-Json;

    if (-not [string]::IsNullOrWhiteSpace($json.RepoUrl))             { $result.repoUrl = $json.RepoUrl.Trim(); }
    if (-not [string]::IsNullOrWhiteSpace($json.PackageTitle))        { $result.packageFileTitle = $json.PackageTitle.Trim(); }
    if (-not [string]::IsNullOrWhiteSpace($json.InstallationPageUrl)) { $result.installationPageUrl = $json.InstallationPageUrl.Trim(); }

    # Distributions keyed by store (google | web); member access is case-insensitive.
    foreach ($store in @('google', 'web')) {
        $dist = $json.Distributions.$store;
        if ($null -eq $dist) { continue; }
        if (-not [string]::IsNullOrWhiteSpace($dist.PackageId))     { $result.packageId[$store] = $dist.PackageId.Trim(); }
        if (-not [string]::IsNullOrWhiteSpace($dist.KeystoreAlias)) { $result.keystoreAlias[$store] = $dist.KeystoreAlias.Trim(); }
    }
    return $result;
}

# Loads optional publish config stored as one-value-per-file under .user/<packageFileTitle>/, matching
# the rest of .user (keystores, tokens) so each value maps 1:1 to a GitHub variable and CI can
# materialize it by writing a single file — no JSON to assemble. Anyone who forks the repo can build
# their OWN apps without editing a committed file. Layout (all optional; blank/absent = project default):
#
#   .user/<packageFileTitle>/repo-url.txt            release repo for this app (per app)
#   .user/<packageFileTitle>/package-title.txt       artifact title override (per app; renames output only)
#   .user/<packageFileTitle>/<store>/package-id.txt  built application id (per store: google | web)
#
# packageId is per store the same way keystores are: 'google' (the Play AAB) and 'web' (the web +
# arm64-web APKs). Windows/Linux builds have no packageId. The title override renames published
# artifacts only; .user folder lookups (keystores, these files) stay keyed by the default folder name,
# and Linux artifact names come from the csproj AssemblyName so the title does not apply there. These
# are non-secret build settings (GitHub *variables*, not secrets).

function Get-UserConfigValue([string]$path) {
    if (Test-Path $path) {
        $v = (Get-Content $path -Raw).Trim();
        if (-not [string]::IsNullOrWhiteSpace($v)) { return $v; }
    }
    return $null;
}

function Get-AppPublishConfig {
    param([Parameter(Mandatory = $true)][string]$packageFileTitle)

    $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot);
    $appDir = Join-Path "$repoRoot/../.user" $packageFileTitle;

    $result = @{ repoUrl = $null; packageFileTitle = $null; packageId = @{} };
    $result.repoUrl          = Get-UserConfigValue (Join-Path $appDir "repo-url.txt");
    $result.packageFileTitle = Get-UserConfigValue (Join-Path $appDir "package-title.txt");
    foreach ($store in @('google', 'web')) {
        $val = Get-UserConfigValue (Join-Path $appDir "$store/package-id.txt");
        if ($val) { $result.packageId[$store] = $val; }
    }
    return $result;
}

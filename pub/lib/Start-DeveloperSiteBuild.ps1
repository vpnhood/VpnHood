# Starts the build of developer.vpnhood.com, which vpnhood/VpnHood.Developer builds from this repo's develop. The release
# scripts call it once a release has bumped the version, so the site documents what was released. It uses the
# releaser's own gh login, as the scripts do for the brand repos' releases: this repo holds no credential for another
# repo. Only vpnhood/VpnHood's own releases start it, and a build that does not start leaves the release as it is: it
# warns, never throws.
function Start-DeveloperSiteBuild {
    param([string]$monoRepo)

    if ($monoRepo -ne "vpnhood/VpnHood") { return; }
    Write-Host "Starting the build of developer.vpnhood.com ..." -ForegroundColor Cyan;
    gh workflow run build.yml --repo vpnhood/VpnHood.Developer --ref main;
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "The build of developer.vpnhood.com did not start; start it at https://github.com/vpnhood/VpnHood.Developer/actions/workflows/build.yml";
    }
}

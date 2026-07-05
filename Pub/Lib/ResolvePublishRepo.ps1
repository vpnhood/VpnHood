# Resolves the GitHub repository that releases are published to, WITHOUT any side effects, so it can
# be dot-sourced from the lightweight per-app _publish.ps1 wrappers (which must NOT source Common.ps1,
# because that bumps the version). The generated *.json install/update URLs are built from this, so
# the resolved repo MUST match where `gh release create` actually puts the release.
#
# Precedence (client / default):
#   VH_PUBLISH_REPO  ->  GITHUB_REPOSITORY (CI)  ->  `git remote get-url origin`  ->  template URL
# Connect is built in this same repo but may release elsewhere:
#   VH_CONNECT_PUBLISH_REPO  ->  (falls through to the client/default chain above)
# So a fork with no env set publishes BOTH client and connect to itself (its origin remote on a
# desktop, or GITHUB_REPOSITORY in CI). When NOTHING resolves (e.g. a non-GitHub clone), we fall back
# to an obvious placeholder URL instead of the production repo: the desktop build still succeeds and
# lands in Pub/bin, and the placeholder in the generated *.json makes it clear it must be configured
# (per-app .user/app-publish/<project>.json repoUrl, or VH_PUBLISH_REPO / VH_CONNECT_PUBLISH_REPO).

$script:PublishRepoServerUrl =
    if ($env:GITHUB_SERVER_URL) { $env:GITHUB_SERVER_URL.TrimEnd('/') } else { "https://github.com" };

# Obvious placeholder used only when nothing else resolves; signals "configure your repo".
$script:PublishRepoTemplateUrl = "https://your-company-domain/your-product";

# Returns the owner/repo slug of the release target, or $null when nothing resolves (caller then
# uses the template URL).
function Resolve-PublishRepoSlug {
    param([switch]$Connect)

    if ($Connect -and -not [string]::IsNullOrWhiteSpace($env:VH_CONNECT_PUBLISH_REPO)) {
        return $env:VH_CONNECT_PUBLISH_REPO.Trim();
    }
    if (-not [string]::IsNullOrWhiteSpace($env:VH_PUBLISH_REPO)) {
        return $env:VH_PUBLISH_REPO.Trim();
    }
    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_REPOSITORY)) {
        return $env:GITHUB_REPOSITORY.Trim();   # CI: owner/repo of the running repo
    }

    # local desktop: derive owner/repo from the origin remote (https or ssh form)
    $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot);
    $origin = (& git -C $repoRoot remote get-url origin 2>$null);
    if ($origin -and ($origin -match 'github\.com[:/]+(?<slug>[^/]+/[^/]+?)(?:\.git)?/?\s*$')) {
        return $Matches['slug'];
    }

    return $null;
}

# Returns the full https URL of the release target, e.g. https://github.com/<owner>/<repo>, or the
# placeholder template URL when nothing resolves.
function Resolve-PublishRepoUrl {
    param([switch]$Connect)
    $slug = Resolve-PublishRepoSlug -Connect:$Connect;
    if ([string]::IsNullOrWhiteSpace($slug)) { return $script:PublishRepoTemplateUrl; }
    return "$script:PublishRepoServerUrl/$slug";
}

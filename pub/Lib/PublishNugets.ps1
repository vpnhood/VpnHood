param(
	# Smoke test: pack & push THROWAWAY prerelease packages (X.Y.Z.<revision>-prerelease) instead of
	# the stable release version, to validate the pipeline without burning a version. See RELEASE-STRATEGY.md.
	[switch]$smoke,
	[string]$revision
);

. "$PSScriptRoot/Common.ps1"

if ($smoke) {
	if ([string]::IsNullOrWhiteSpace($revision)) {
		$revision = if ($env:GITHUB_RUN_NUMBER) { $env:GITHUB_RUN_NUMBER } else { (Get-Date).ToString("MMddHHmm") };
	}
	$nugetVersion = "$versionParam.$revision-prerelease";
	Write-Host "*** NUGET SMOKE TEST: publishing PRERELEASE $nugetVersion (base version untouched, no commit)" -BackgroundColor DarkMagenta;
}
else {
	$nugetVersion = "$versionParam";
}

# Publishing requires a key (the CI job runs only in the vpnhood org where the secret is set).
if ([string]::IsNullOrWhiteSpace($nugetApiKey)) {
	throw "PublishNugets: NuGet API key is missing. Set the NUGET_API_KEY secret (CI) or .user/nuget_api_key.txt (local).";
}

# Discover packable projects: a project IS a package unless it opts out with <IsPackable>false</IsPackable>.
$projectFiles = Get-ChildItem -Path "$solutionDir/src" -Recurse -File -Filter "*.csproj" |
	Where-Object { [System.IO.File]::ReadAllText($_.FullName) -notmatch "(?i)<IsPackable>\s*false\s*</IsPackable>" } |
	Sort-Object FullName;
Write-Host "Discovered $($projectFiles.Count) packable project(s) under src." -ForegroundColor Cyan;

# Write a throwaway solution of ONLY those projects at the repo root (so its relative paths resolve),
# then pack it in ONE MSBuild pass: shared dependencies build once and projects pack in parallel,
# instead of launching ~50 separate `dotnet pack` processes. -p:Version stamps every package.
$packDir = Join-Path $pubDir "bin/nuget";
Remove-Item $packDir -Recurse -Force -ErrorAction Ignore;
New-Item -ItemType Directory -Path $packDir -Force | Out-Null;

$tmpSln = Join-Path $solutionDir "_nuget_pack.slnx";
$sb = [System.Text.StringBuilder]::new();
[void]$sb.AppendLine('<Solution>');
foreach ($p in $projectFiles) {
	$rel = [System.IO.Path]::GetRelativePath($solutionDir, $p.FullName).Replace('\', '/');
	[void]$sb.AppendLine("  <Project Path=`"$rel`" />");
}
[void]$sb.AppendLine('</Solution>');
Set-Content -LiteralPath $tmpSln -Value $sb.ToString() -Encoding utf8;

try {
	Write-Host "Packing $($projectFiles.Count) projects in one pass -> $packDir" -ForegroundColor Cyan;
	dotnet pack $tmpSln -c Release -o $packDir `
		-p:Version=$nugetVersion -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg -p:SolutionDir=$solutionDir;
	if ($LASTEXITCODE -gt 0) { throw "dotnet pack failed with exit code $LASTEXITCODE."; }
}
finally {
	Remove-Item $tmpSln -Force -ErrorAction Ignore;
}

# Push everything produced (pushing a .nupkg also pushes its adjacent .snupkg symbols).
$packages = Get-ChildItem -Path $packDir -File | Where-Object { $_.Extension -eq ".nupkg" };
Write-Host "Pushing $($packages.Count) package(s)..." -ForegroundColor Cyan;
foreach ($pkg in $packages) {
	dotnet nuget push $pkg.FullName --source "https://api.nuget.org/v3/index.json" --api-key $nugetApiKey --skip-duplicate;
	if ($LASTEXITCODE -gt 0) { Write-Host "push failed: $($pkg.Name)" -ForegroundColor Red; }
}

param(
	# Smoke test: pack & push THROWAWAY prerelease packages (X.Y.Z.<revision>-prerelease) instead of
	# the stable release version, to validate the pipeline without burning a version. See RELEASE-STRATEGY.md.
	[switch]$smoke,
	[string]$revision
);

. "$PSScriptRoot/Common.ps1"
# Optional Authenticode signing of the packaged assemblies (same pair rule and identity as the
# Windows app build — see SignFiles.ps1). Absent credentials -> unsigned packages, exactly as before.
. "$PSScriptRoot/SignFiles.ps1"

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
	throw "Publish-NugetPackages: NuGet API key is missing. Set the NUGET_API_KEY secret (CI) or .user/nuget_api_key.txt (local).";
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
	# MSBuild keeps its worker nodes alive after a build returns so the next build can reuse them, and
	# those nodes hold open handles on the Android .aar outputs. A single `dotnet pack` never noticed
	# (one MSBuild session did everything), but signing forces build and pack into TWO invocations, and
	# the second then dies with "XARLP7024: The file is locked by MSBuild.dll". Disable node reuse so
	# every invocation's nodes exit with it.
	$env:MSBUILDDISABLENODEREUSE = "1";

	# Build first, then sign the outputs, then pack WITHOUT rebuilding — a plain `dotnet pack` would
	# compile and zip in one pass, leaving no point at which the assemblies exist on disk unsigned-yet-
	# unpacked. -p:Version must match between the two calls so pack picks up exactly what build produced.
	Write-Host "Building $($projectFiles.Count) projects in one pass..." -ForegroundColor Cyan;
	dotnet build $tmpSln -c Release `
		-p:Version=$nugetVersion -p:SolutionDir=$solutionDir;
	if ($LASTEXITCODE -gt 0) { throw "dotnet build failed with exit code $LASTEXITCODE."; }

	# Sign each project's OWN assembly (per TFM), so consumers get publisher-verifiable DLLs. Matching
	# on the project file's base name deliberately skips the dependency COPIES strewn across bin dirs
	# (each project's bin holds copies of every VpnHood.* it references — signing those would burn
	# quota on files that never enter a package) and the ref/ reference assemblies (compile-time only,
	# not packed). Invoke-VhSign additionally drops anything already signed; no-op when signing is off.
	$packAssemblies = foreach ($p in $projectFiles) {
		$binDir = Join-Path $p.DirectoryName "bin/Release";
		if (Test-Path $binDir) {
			Get-ChildItem $binDir -Recurse -File -Filter "$($p.BaseName).dll" |
				Where-Object { $_.FullName -notmatch "[\\/]ref[\\/]" } |
				Select-Object -ExpandProperty FullName;
		}
	}
	Invoke-VhSign $packAssemblies;

	Write-Host "Packing $($projectFiles.Count) projects (no rebuild) -> $packDir" -ForegroundColor Cyan;
	dotnet pack $tmpSln -c Release -o $packDir --no-build `
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

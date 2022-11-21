. "$PSScriptRoot/../Pub/Common.ps1"

Write-Host;
Write-Host "*** Building Client Windows ..." -BackgroundColor Blue -ForegroundColor White;

# Build project
$projectDir = $PSScriptRoot;
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;
$publishDir = "$projectDir/bin/release/publish";
$targetFramework = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.TargetFramework;

#update project version
UpdateProjectVersion $projectFile;

# publish 
Write-Host;
if (-not $noclean)  { dotnet clean "$projectDir" -c "Release" --output $publishDir /verbosity:minimal }
dotnet publish "$projectDir" -c "Release" --output $publishDir --framework $targetFramework --no-self-contained /p:Version=$versionParam
if ($LASTEXITCODE -gt 0) { Throw "The publish exited with error code: " + $lastexitcode; }

# Build Setup
& "$solutionDir/VpnHood.Client.App.Win.Setup/_publish.ps1";

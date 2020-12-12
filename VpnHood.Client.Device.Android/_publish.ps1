. "$PSScriptRoot\..\Pub\Common.ps1"

$projectDir = $PSScriptRoot
$projectFile = (Get-ChildItem -path $projectDir -file -Filter "*.csproj").FullName;
$publishDir = Join-Path $projectDir "bin/release/publish/"
$nuget = Join-Path $projectDir "Nuget/nuget.exe";
$nugetSpec = Join-Path $projectDir "Nuget/nuget.nuspec";
$packageId = ([Xml] (Get-Content $projectFile)).Project.PropertyGroup.AssemblyName
$packageId = ([string]$packageId).Trim()

# bundle (aab)
& $msbuild $projectFile /p:Configuration=Release /p:Version=$versionParam;

 # Create launcher
Write-Host;
Write-Host "*** Creating the nuget..." -BackgroundColor Blue -ForegroundColor White;
 & $nuget pack $nugetSpec -version $versionParam -Symbols -BasePath $projectDir -OutputDirectory $publishDir -NonInteractive

# publish nuget
Write-Host
Write-Host "*** Publishing..." -BackgroundColor Blue
$packageFile = Join-Path $publishDir "$packageId.$versionParam.nupkg"
dotnet nuget push $packageFile --api-key $nugetApiKey --source https://api.nuget.org/v3/index.json
if ($LASTEXITCODE -gt 0)
{
    Throw "The publish exited with error code: " + $lastexitcode
}

# report version
ReportVersion

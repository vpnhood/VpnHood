param(
	[String]$versionFile,
	[int]$bump
)

$versionJson = (Get-Content $versionFile | Out-String | ConvertFrom-Json);
$bumpTime = [datetime]::Parse($versionJson.BumpTime);
$version = [version]::Parse($versionJson.Version);
if ( $bump -gt 0 )
{
	$isVersionBumped = $true;
	$version = [version]::new($version.Major, $version.Minor, $version.Build + 1);
	$versionJson.Version = $version.ToString(3);
	$versionJson.BumpTime = [datetime]::UtcNow.ToString("o");
	$versionJson.Prerelease = ($bump -eq 2);
	$versionJson | ConvertTo-Json -depth 10 | Out-File $versionFile;
}

$prerelease = $versionJson.Prerelease;
$isLatest = $versionJson.Prerelease -eq $false; 
$versionParam = $version.ToString(3);
$versionTag = "v$versionParam" + (&{if($prerelease) {"-prerelease"} else {""}});
$releaseDate = Get-Date -asUTC -Format "s";
$deprecatedVersion = $versionJson.DeprecatedVersion;

if ( $bump -gt 0 )
{
	Write-Host "Version has been bumped to: $versionParam" -ForegroundColor Blue;
}

# ReportVersion
Function ReportVersion() 
{
	Write-Host "version: $versionParam" -ForegroundColor GREEN;
}

# UpdateProjectVersion
Function UpdateProjectVersion([string] $projectFile) 
{
	$xml = New-Object XML;
	$xml.PreserveWhitespace = $true;
	$xml.Load($projectFile);
	$fileVersion = $xml.SelectSingleNode("Project/PropertyGroup/FileVersion");
	$packageVersion = $xml.SelectSingleNode("Project/PropertyGroup/Version");

	if ($packageVersion -and $packageVersion.InnerText -ne $versionParam){
		$fileVersion.InnerText = '$([System.DateTime]::Now.ToString("yyyy.M.d.HHmm"))';
		$packageVersion.InnerText = $versionParam;

		# Update Android Version
		$applicationVersion = $xml.SelectSingleNode("Project/PropertyGroup/ApplicationVersion");
		$applicationDisplayVersion = $xml.SelectSingleNode("Project/PropertyGroup/ApplicationDisplayVersion");
		if ($applicationVersion)
		{
			$applicationVersion.InnerText = $version.Build;
			$applicationDisplayVersion.InnerText = $versionParam;
		}

		# Update project file
		$xml.Save($projectFile);
	}
}

Function Copy-ZipAndUnzipRemote {
    param (
        [string]$localDir,
        [string]$remoteDir,
        [string]$remote,
        [string]$userPrivateKeyFile,
        [string]$remoteUser
    )

    # Compress the output directory
    $tarFile =  Join-Path (Split-Path $localDir -Parent) "_output.tar";
    Write-Host "Compressing the output directory..."
    tar -czf $tarFile -C $localDir *;

    # Create directory and set permissions on the remote server
	$remoteDir = "$remoteDir/" + (Split-Path $localDir -Leaf);
    ssh -i $userPrivateKeyFile $remote "sudo mkdir -p $remoteDir; sudo chown $remoteUser $remoteDir";

    # Upload the compressed file
    $remoteTar = "$remoteDir/output.tar";
    Write-Host "Uploading the compressed file...";
    scp -i $userPrivateKeyFile $tarFile ($remote + ":$remoteTar");

    # Extract the file on the remote server
    Write-Host "Extracting the files on the remote server...";
	$remoteFolder = Split-Path $localDir -Leaf
    ssh -i $userPrivateKeyFile $remote "tar -xzf $remoteTar -C $remoteDir";

    # Delete the compressed file on the remote server
    Write-Host "Deleting compressed files";
    ssh -i $userPrivateKeyFile $remote "rm $remoteTar";
    Remove-Item $tarFile;
}

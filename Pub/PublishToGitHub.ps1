param(
	[switch]$prerelease
);

. "$PSScriptRoot/Common.ps1"

# update CHANGELOG
$text = Get-Content "$solutionDir/CHANGELOG.md" -Raw;
# if ( $text.IndexOf("# Upcoming") -eq -1) { throw "Could not find # Upcoming phrase in CHANGELOG" };
$changeLog = $text -replace "# Upcoming", "# v$versionParam";
$changeLog  | Out-File -FilePath "$solutionDir/CHANGELOG.md" -Encoding utf8 -Force;

# create release note
$releaseNote = $text -replace "# Upcoming", "$versionTag`n";
$releaseNote = $releaseNote.SubString(0, $releaseNote.IndexOf("`n# "));
# $releaseNote += "To see a list of all changes visit: [Changelog](https://github.com/vpnhood/VpnHood/blob/main/CHANGELOG.md)";
$releaseNote  | Out-File -FilePath "$packagesRootDir/ReleaseNote.txt" -Encoding utf8 -Force;

# commit and push git
$gitDir = "$solutionDir/.git";
git --git-dir=$gitDir --work-tree=$solutionDir commit -a -m "Publish v$versionParam";
git --git-dir=$gitDir --work-tree=$solutionDir pull;
git --git-dir=$gitDir --work-tree=$solutionDir push;

# swtich to main branch
if (!$prerelease)
{
	git --git-dir=$gitDir --work-tree=$solutionDir checkout main
	git --git-dir=$gitDir --work-tree=$solutionDir pull;
	git --git-dir=$gitDir --work-tree=$solutionDir merge development;
	git --git-dir=$gitDir --work-tree=$solutionDir push;
	git --git-dir=$gitDir --work-tree=$solutionDir checkout development
}

# publish using github CLI: https://github.com/github/hub
# Use --prerelease for prerelease!
Push-Location -Path "$solutionDir";
gh release create "$versionTag"`
	--title "$versionTag" `
	(&{if($prerelease) {"-prerelease"} else {""}}) `
	-F $packagesRootDir/ReleaseNote.txt `
	$packagesClientDir/VpnHoodClient-Android.apk `
	$packagesClientDir/VpnHoodClient-win.exe  `
	$packagesClientDir/VpnHoodClient-win.txt  `
	$packagesServerDir/VpnHoodServer.json `
	$packagesServerDir/VpnHoodServer.zip `
	$packagesServerDir/VpnHoodServer.tar.gz `
	$packagesServerDir/VpnHoodServer.docker.yml `
	$packagesServerDir/VpnHoodServer.docker.sh `
	$packagesServerDir/install-linux.sh;
Pop-Location


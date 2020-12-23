. "$PSScriptRoot/Common.ps1"

$tag="v$versionParam-beta"

# update CHANGELOG
$text = Get-Content "$solutionDir/CHANGELOG.md" -Raw;
if ( $text.IndexOf("# Upcoming") -eq -1) { throw "Could not find #Upcoming phrase in CHANGELOG" };
$changeLog = $text -replace "# Upcoming", "# v$versionParam";
$changeLog  | Out-File -FilePath "$solutionDir/CHANGELOG.md" -Encoding utf8 -Force;

# create release note
$releaseNote = $text -replace "# Upcoming", "$tag`n";
$releaseNote = $releaseNote.SubString(0, $releaseNote.IndexOf("`n# "));
# $releaseNote += "To see a list of all changes visit: [Changelog](https://github.com/vpnhood/VpnHood/blob/main/CHANGELOG.md)";
$releaseNote  | Out-File -FilePath "$packagesRoorDir/ReleaseNote.txt" -Encoding utf8 -Force;

# commit and push git
$gitDir = "$solutionDir/.git";
git --git-dir=$gitDir --work-tree=$solutionDir commit -a -m "Publish v$versionParam";
git --git-dir=$gitDir --work-tree=$solutionDir pull;
git --git-dir=$gitDir --work-tree=$solutionDir push;

# publish using github CLI: https://github.com/github/hub
hub --git-dir=$gitDir --work-tree=$solutionDir release create `
	-a $packagesClientDir/VpnHoodClient-Android.apk `
	-a $packagesClientDir/VpnHoodClient-win.exe  `
	-a $packagesClientDir/VpnHoodClient-win.json `
	-a $packagesClientDir/VpnHoodClient-win.zip `
	-a $packagesServerDir/VpnHoodServer.json `
	-a $packagesServerDir/VpnHoodServer.zip `
	-a $packagesRootDir/ReleaseNote.txt `
	-F $packagesRootDir/ReleaseNote.txt `
	--prerelease "$tag";

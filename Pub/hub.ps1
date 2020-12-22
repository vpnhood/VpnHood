. "$PSScriptRoot\Common.ps1"
$releaseDir="$PSScriptRoot\bin";

# update CHANGELOG
$text = Get-Content "$solutionDir\CHANGELOG.md" -Raw;
if ( $text.IndexOf("# Upcoming") -eq -1) { throw "Could not find #Upcoming phrase in CHANGELOG" };
$changeLog = $text -replace "# Upcoming", "# $versionParam";
$changeLog  | Out-File -FilePath "$solutionDir\CHANGELOG.md" -Encoding utf8 -Force;

# create release note
$releaseNote = $text -replace "# Upcoming", "v$versionParam";
$releaseNote = $releaseNote.SubString(0, $releaseNote.IndexOf("`n# "));
$releaseNote += "To see a list of all changes visit:`n[ChangeLog](https://github.com/vpnhood/VpnHood/blob/main/CHANGELOG.md)";
$releaseNote  | Out-File -FilePath "$releaseDir\ReleaseNote.txt" -Encoding utf8 -Force;

# commit and push git
$gitDir = "$solutionDir\.git";
git --git-dir=$gitDir --work-tree=$solutionDir commit -a -m "Publish v$versionParam";
git --git-dir=$gitDir --work-tree=$solutionDir pull;
git --git-dir=$gitDir --work-tree=$solutionDir push;

# publish using github CLI: https://github.com/github/hub
 hub --git-dir=$gitDir --work-tree=$solutionDir release create `
	-a $releaseDir/VpnHoodClient-Android.apk `
	-a $releaseDir/VpnHoodClient-win.exe  `
	-a $releaseDir/VpnHoodClient-win.json `
	-a $releaseDir/VpnHoodClient-win.zip `
	-a $releaseDir/VpnHoodServer.json `
	-a $releaseDir/VpnHoodServer.zip `
	-a $releaseDir/ReleaseNote.txt `
	-F $releaseDir/ReleaseNote.txt `
	"v$versionParam";

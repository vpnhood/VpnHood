. "$PSScriptRoot\Common.ps1"
$ReleaseDir="$PSScriptRoot\bin";

# create release note
$text = Get-Content "$solutionDir\CHANGELOG.md" -Raw;
if ( $text.IndexOf("# Upcoming") -eq -1) { throw "Could not find #Upcoming phrase in CHANGELOG" };
$text = $text -replace "# Upcoming", "# v$versionParam";
$text = $text.SubString(0, $text.IndexOf("`n# "));
$text += "`n`nFor list of all changes see:`nhttps://github.com/vpnhood/VpnHood/blob/main/CHANGELOG.md";

$text  | Out-File -FilePath "$ReleaseDir\ReleaseNote.txt" -Encoding utf8 -Force;


# commit and push git
$gitDir = "$solutionDir\.git";
cd $solutionDir
git commit -m "Publish v$versionParam"
git pull
git push

exit

# publish using github CLI: https://github.com/github/hub
 # hub release create `
	-a bin/VpnHoodClient-Android.apk `
	-a bin/VpnHoodClient-win.exe  `
	-a bin/VpnHoodClient-win.json `
	-a bin/VpnHoodClient-win.zip `
	-a bin/VpnHoodServer.json `
	-a bin/VpnHoodServer.zip `
	-a bin/ReleaseNote.txt `
	-F bin/ReleaseNote.txt `
	"v$versionParam";

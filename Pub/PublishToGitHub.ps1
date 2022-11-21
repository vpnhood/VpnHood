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
$releaseNote | Out-File -FilePath "$packagesRootDir/ReleaseNote.txt" -Encoding utf8 -Force;
if ($isLatest)
{
	$releaseNote | Out-File -FilePath "$packagesRootDirLatest/ReleaseNote.txt" -Encoding utf8 -Force;
}

# commit and push git
$gitDir = "$solutionDir/.git";
git tag --delete "$versionTag";
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
$releaseRootDir = (&{if($isLatest) {$packagesRootDirLatest} else {$packagesRootDir}})
$releaseClientDir = (&{if($isLatest) {$packagesClientDirLatest} else {$packagesClientDir}})
$releaseServerDir = (&{if($isLatest) {$packagesServerDirLatest} else {$packagesServerDir}})

gh release delete "$versionTag" --cleanup-tag --yes;
gh release create "$versionTag" `
	--title "$versionTag" `
	(&{if($prerelease) {"--prerelease"} else {""}}) `
	-F $releaseRootDir/ReleaseNote.txt `
	$releaseServerDir/docker/VpnHoodServer.docker.sh;
	;

Pop-Location
exit
gh release create "$versionTag" `
	--title "$versionTag" `
	(&{if($prerelease) {"--prerelease"} else {""}}) `
	-F $releaseRootDir/ReleaseNote.txt `
	$releaseClientDir/android/VpnHoodClient-Android.apk `
	$releaseClientDir/windows/VpnHoodClient-win.exe  `
	$releaseClientDir/windows/VpnHoodClient-win.txt  `
	$releaseServerDir/linux-x64/VpnHoodServer-linux.json `
	$releaseServerDir/linux-x64/VpnHoodServer-linux.sh `
	$releaseServerDir/linux-x64/VpnHoodServer-linux.tar.gz `
	$releaseServerDir/docker/VpnHoodServer.docker.yml `
	$releaseServerDir/docker/VpnHoodServer.docker.sh;
	;
Pop-Location


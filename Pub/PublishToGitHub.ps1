. "$PSScriptRoot/Core/Common.ps1"

# update CHANGELOG
$text = Get-Content "$solutionDir/CHANGELOG.md" -Raw;
# if ( $text.IndexOf("# Upcoming") -eq -1) { throw "Could not find # Upcoming phrase in CHANGELOG" };
$changeLog = $text -replace "# Upcoming", "# v$versionParam";
$changeLog  | Out-File -FilePath "$solutionDir/CHANGELOG.md" -Encoding utf8 -Force -NoNewline;

# create release note
$releaseNote = $changeLog;
$releaseNote = $releaseNote.SubString($releaseNote.IndexOf("`n")); # remove version tag
$releaseNote = $releaseNote.SubString(0, $releaseNote.IndexOf("`n# ")); # remove other version
# $releaseNote += "To see a list of all changes visit: [Changelog](https://github.com/vpnhood/VpnHood/blob/main/CHANGELOG.md)";
$releaseNote | Out-File -FilePath "$packagesRootDir/ReleaseNote.txt" -Encoding utf8 -Force -NoNewline;
if ($isLatest)
{
	$releaseNote | Out-File -FilePath "$packagesRootDirLatest/ReleaseNote.txt" -Encoding utf8 -Force -NoNewline;
}

# replace all final version to current repo
UpdateRepoVersionInFile;

# Publishing to GitHub
Push-Location -Path "$solutionDir";

# commit and push git
$gitDir = "$solutionDir/.git";
gh release delete "$versionTag" --cleanup-tag --yes;
git --git-dir=$gitDir --work-tree=$solutionDir tag --delete "$versionTag";
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
$releaseRootDir = (&{if($isLatest) {$packagesRootDirLatest} else {$packagesRootDir}})
$androidStore = (&{if($prerelease) {"-web"} else {""}});

gh release create "$versionTag" `
	--title "$versionTag" `
	(&{if($prerelease) {"--prerelease"} else {"--latest"}}) `
	-F $releaseRootDir/ReleaseNote.txt `
	$releaseRootDir/$packageClientDirName/android/VpnHoodClient-android$androidStore.apk `
	$releaseRootDir/$packageClientDirName/android/VpnHoodClient-android$androidStore.json `
	$releaseRootDir/$packageClientDirName/windows/VpnHoodClient-win-x64.msi  `
	$releaseRootDir/$packageClientDirName/windows/VpnHoodClient-win-x64.txt  `
	$releaseRootDir/$packageClientDirName/windows/VpnHoodClient-win-x64.json `
	$releaseRootDir/$packageServerDirName/linux-x64/VpnHoodServer-linux-x64.json `
	$releaseRootDir/$packageServerDirName/linux-x64/VpnHoodServer-linux-x64.sh `
	$releaseRootDir/$packageServerDirName/linux-x64/VpnHoodServer-linux-x64.tar.gz `
	$releaseRootDir/$packageServerDirName/linux-arm64/VpnHoodServer-linux-arm64.json `
	$releaseRootDir/$packageServerDirName/linux-arm64/VpnHoodServer-linux-arm64.sh `
	$releaseRootDir/$packageServerDirName/linux-arm64/VpnHoodServer-linux-arm64.tar.gz `
	$releaseRootDir/$packageServerDirName/win-x64/VpnHoodServer-win-x64.json `
	$releaseRootDir/$packageServerDirName/win-x64/VpnHoodServer-win-x64.ps1 `
	$releaseRootDir/$packageServerDirName/win-x64/VpnHoodServer-win-x64.zip `
	$releaseRootDir/$packageServerDirName/docker/VpnHoodServer.docker.yml `
	$releaseRootDir/$packageServerDirName/docker/VpnHoodServer.docker.sh;

Pop-Location

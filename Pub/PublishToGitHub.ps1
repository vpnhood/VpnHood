. "$PSScriptRoot/Core/Common.ps1"

# update CHANGELOG
$text = Get-Content "$solutionDir/CHANGELOG.md" -Raw;

# find top version
$vStart = $text.IndexOf("#");
$vEnd = $text.IndexOf("`n", $vStart) - 1;
$topVersion = $text.SubString($vStart, $vEnd - $vStart);

# change top version
$changeLog = $text -replace $topVersion, "# v$versionParam";
$changeLog  | Out-File -FilePath "$solutionDir/CHANGELOG.md" -Encoding utf8 -Force -NoNewline;

# create release note
$releaseNote = $changeLog;
$releaseNote = $releaseNote.SubString($releaseNote.IndexOf("`n")); # remove version tag
$releaseNote = $releaseNote.SubString(0, $releaseNote.IndexOf("`n# ")); # remove other version
$releaseNote | Out-File -FilePath "$packagesRootDir/ReleaseNote.txt" -Encoding utf8 -Force -NoNewline;
if ($isLatest)
{
	$releaseNote | Out-File -FilePath "$packagesRootDirLatest/ReleaseNote.txt" -Encoding utf8 -Force -NoNewline;
}

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
$androidGoogleLatestDir = Join-Path $pubDir "Android.GooglePlay/apk/latest";

gh release create "$versionTag" `
	--title "$versionTag" `
	(&{if($prerelease) {"--prerelease"} else {"--latest"}}) `
	-F $releaseRootDir/ReleaseNote.txt `
	$androidGoogleLatestDir/VpnHoodClient-android.apk `
	$androidGoogleLatestDir/VpnHoodClient-android.json `
	$releaseRootDir/$packageClientDirName/android-web/VpnHoodClient-android-web.apk `
	$releaseRootDir/$packageClientDirName/android-web/VpnHoodClient-android-web.json `
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

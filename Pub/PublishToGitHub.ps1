param( 
	[Parameter(Mandatory=$true)][object]$mainRepo,
	[Parameter(Mandatory=$true)][object]$connectRepo
	);

$mainRepo = $mainRepo -eq "1";
$connectRepo = $connectRepo -eq "1";

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
if ($isLatest) {
	$releaseNote | Out-File -FilePath "$packagesRootDirLatest/ReleaseNote.txt" -Encoding utf8 -Force -NoNewline;
}

#-----------
# CLIENT REPO
#-----------
if ($mainRepo) {
	Write-Host "*** Publish the main releases" -BackgroundColor Blue

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
	if (!$prerelease) {
		git --git-dir=$gitDir --work-tree=$solutionDir checkout main
		git --git-dir=$gitDir --work-tree=$solutionDir pull;
		git --git-dir=$gitDir --work-tree=$solutionDir merge development;
		git --git-dir=$gitDir --work-tree=$solutionDir push;
		git --git-dir=$gitDir --work-tree=$solutionDir checkout development
	}

	# publish using github CLI: https://github.com/github/hub
	$androidGoogleLatestDir = Join-Path $pubDir "Android.GooglePlay/apk/latest";

	gh release create "$versionTag" `
		--title "$versionTag" `
		(&{if($prerelease) {"--prerelease"} else {"--latest"}}) `
		-F $releaseRootDir/ReleaseNote.txt `
		$androidGoogleLatestDir/VpnHoodClient-android.apk `
		$androidGoogleLatestDir/VpnHoodClient-android.json `
		$releaseRootDir/$packageClientDirName/android-web/VpnHoodClient-android-web.apk `
		$releaseRootDir/$packageClientDirName/android-web/VpnHoodClient-android-web.json `
		$releaseRootDir/$packageClientDirName/windows-web/VpnHoodClient-win-x64.msi  `
		$releaseRootDir/$packageClientDirName/windows-web/VpnHoodClient-win-x64.txt  `
		$releaseRootDir/$packageClientDirName/windows-web/VpnHoodClient-win-x64.json `
		$packagesRootDirLatest/$packageServerDirName/linux-x64/VpnHoodServer-linux-x64.json `
		$packagesRootDirLatest/$packageServerDirName/linux-arm64/VpnHoodServer-linux-arm64.json `
		$packagesRootDirLatest/$packageServerDirName/win-x64/VpnHoodServer-win-x64.json;

	Pop-Location
}

#-----------
# CONNECT REPO
#-----------
if ($connectRepo) {
	Write-Host "*** Publish VpnHood! CONNECT releases" -BackgroundColor Blue

	# set Connect Variables
	$connectRepoDir = Join-Path $vhDir "VpnHood.App.Connect";
	$androidGoogleLatestDir = Join-Path $connectRepoDir "pub/Android.GooglePlay/apk/latest";
	
	# Publishing to GitHub
	Push-Location -Path $connectRepoDir;

	gh release delete "$versionTag" --cleanup-tag --yes;
	gh release create "$versionTag" `
		--title "$versionTag" `
		(&{if($prerelease) {"--prerelease"} else {"--latest"}}) `
		-F $releaseRootDir/ReleaseNote.txt `
		$androidGoogleLatestDir/VpnHoodConnect-android.apk `
		$androidGoogleLatestDir/VpnHoodConnect-android.json `
		$releaseRootDir/$packageConnectDirName/android-web/VpnHoodConnect-android-web.apk `
		$releaseRootDir/$packageConnectDirName/android-web/VpnHoodConnect-android-web.json `
		$releaseRootDir/$packageConnectDirName/windows-web/VpnHoodConnect-win-x64.msi `
		$releaseRootDir/$packageConnectDirName/windows-web/VpnHoodConnect-win-x64.txt `
		$releaseRootDir/$packageConnectDirName/windows-web/VpnHoodConnect-win-x64.json;
	
	Pop-Location
}
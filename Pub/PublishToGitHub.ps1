param( 
	[Parameter(Mandatory=$true)][object]$mainRepo,
	[Parameter(Mandatory=$true)][object]$connectRepo
	);

$mainRepo = $mainRepo -eq "1";
$connectRepo = $connectRepo -eq "1";

. "$PSScriptRoot/Core/Common.ps1"
. "$PSScriptRoot/Core/utils/changelog_utils.ps1"

# update CHANGELOG
$text = Get-Content "$solutionDir/CHANGELOG.md" -Raw;

# find top version
$text = Changelog_UpdateHeader $text "v$versionParam"
$changeLog | Out-File -FilePath "$solutionDir/CHANGELOG.md" -Encoding utf8 -Force -NoNewline;

# create release note
$releaseNote = Changelog_GetRecentSecion $changeLog;
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
	PushMainRepo;

	gh release delete "$versionTag" --cleanup-tag --yes > $null 2>&1;
	gh release create "$versionTag" `
		--title "$versionTag" `
		(&{if($prerelease) {"--prerelease"} else {"--latest"}}) `
		-F $releaseRootDir/ReleaseNote.txt `
		$releaseRootDir/$packageClientDirName/Linux-any/VpnHoodClient-linux.sh `
		$releaseRootDir/$packageClientDirName/Linux-x64/VpnHoodClient-linux-x64.sh `
		$releaseRootDir/$packageClientDirName/Linux-x64/VpnHoodClient-linux-x64.json `
		$releaseRootDir/$packageClientDirName/Linux-x64/VpnHoodClient-linux-x64.tar.gz `
		$releaseRootDir/$packageClientDirName/Linux-arm64/VpnHoodClient-linux-arm64.sh `
		$releaseRootDir/$packageClientDirName/Linux-arm64/VpnHoodClient-linux-arm64.json `
		$releaseRootDir/$packageClientDirName/Linux-arm64/VpnHoodClient-linux-arm64.tar.gz `
		$releaseRootDir/$packageClientDirName/android-web/VpnHoodClient-android-web.apk `
		$releaseRootDir/$packageClientDirName/android-web/VpnHoodClient-android-web.json `
		$releaseRootDir/$packageClientDirName/windows-web/VpnHoodClient-win-x64.msi  `
		$releaseRootDir/$packageClientDirName/windows-web/VpnHoodClient-win-x64.txt  `
		$releaseRootDir/$packageClientDirName/windows-web/VpnHoodClient-win-x64.json;

	Pop-Location
}

#-----------
# CONNECT REPO
#-----------
if ($connectRepo) {
	Write-Host "*** Publish VpnHood! CONNECT releases" -BackgroundColor Blue

	# set Connect Variables
	$connectRepoDir = Join-Path $vhDir "VpnHood.App.Connect";
	
	# Publishing to GitHub
	Push-Location -Path $connectRepoDir;
	gh release create "$versionTag" `
		--title "$versionTag" `
		(&{if($prerelease) {"--prerelease"} else {"--latest"}}) `
		-F $releaseRootDir/ReleaseNote.txt `
		$releaseRootDir/$packageConnectDirName/android-google/VpnHoodConnect-android.aab `
		$releaseRootDir/$packageConnectDirName/android-google/VpnHoodConnect-android.aab.json `
		$releaseRootDir/$packageConnectDirName/android-web/VpnHoodConnect-android-web.apk `
		$releaseRootDir/$packageConnectDirName/android-web/VpnHoodConnect-android-web.json `
		$releaseRootDir/$packageConnectDirName/linux-x64/VpnHoodConnect-linux-x64.tar.gz `
		$releaseRootDir/$packageConnectDirName/linux-x64/VpnHoodConnect-linux-x64.json `
		$releaseRootDir/$packageConnectDirName/linux-x64/VpnHoodConnect-linux-x64.sh `;
		$releaseRootDir/$packageConnectDirName/linux-arm64/VpnHoodConnect-linux-arm64.tar.gz `
		$releaseRootDir/$packageConnectDirName/linux-arm64/VpnHoodConnect-linux-arm64.json `;
		$releaseRootDir/$packageConnectDirName/linux-arm64/VpnHoodConnect-linux-arm64.sh `;
		$releaseRootDir/$packageConnectDirName/linux-any/VpnHoodConnect-linux.sh `;
		$releaseRootDir/$packageConnectDirName/windows-web/VpnHoodConnect-win-x64.msi `
		$releaseRootDir/$packageConnectDirName/windows-web/VpnHoodConnect-win-x64.json `;
		$releaseRootDir/$packageConnectDirName/windows-web/VpnHoodConnect-win-x64.txt;
	
	Pop-Location
}
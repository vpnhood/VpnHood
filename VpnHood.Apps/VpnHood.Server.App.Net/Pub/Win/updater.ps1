$curDir = $PSScriptRoot;

Start-Transcript "$curDir/storage/logs/update.log";
try
{
    Write-Output "Updating VpnHood Server for Windows...";
    $ErrorActionPreference = "Stop";
    [Net.ServicePointManager]::SecurityProtocol = "Tls, Tls11, Tls12";
    $localPublishInfoFile = "$curDir/publish.json";

    # -------------------
    # Check for Update
    # -------------------

    # load local publish info
    $localPublishInfo = (Get-Content $localPublishInfoFile | Out-String | ConvertFrom-Json);
    $localVersion = $localPublishInfo.Version;
    $localUpdateCode = $localPublishInfo.UpdateCode;
    $localUpdateInfoUrl = $localPublishInfo.UpdateInfoUrl;

    # Check is latest version available
    if ("$localVersion" -eq ""){
        throw "Could not load the installed version information! Path: $localPublishInfoFile";
    }

    # load online publish info
    $onlinePublishInfo = Invoke-RestMethod -Uri $localUpdateInfoUrl;
    $onlineVersion = $onlinePublishInfo.Version;
    $onlineUpdateCode = $onlinePublishInfo.UpdateCode;
    $onlineInstallScriptUrl = $onlinePublishInfo.InstallScriptUrl;

    # Compare the update code
    if ( "$localUpdateCode" -ne "$onlineUpdateCode" ){
        throw "The installed version can not be updated. You need to update it manually!";
    }

    # Compare Version
    Write-Output "Installed version: $localVersion";
    Write-Output "Latest version: $onlineVersion";
    if ([Version]$localVersion -ge [Version]"$onlineVersion") {
        Write-Output "The installed version is up to date.";
        exit;
    }

    # Install the new version
    Write-Output "Installing the latest version...";
    & ([scriptblock]::Create((Invoke-RestMethod -Uri $onlineInstallScriptUrl))) -q -autostart;
}
finally
{
    Stop-Transcript | Out-Null ;
}

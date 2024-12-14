# Set the root directory
$rootDir = "VpnHood.AppFramework.Android.Common"

# Rename folders
Get-ChildItem -Path $rootDir -Recurse -Directory -Force | ForEach-Object {
    if ($_.Name -like "*AppFramework*") {
        $newName = $_.Name -replace "AppFramework", "AppLibs"
        Rename-Item -Path $_.FullName -NewName $newName
    }
}

# Rename files
Get-ChildItem -Path $rootDir -Recurse -File -Force | ForEach-Object {
    if ($_.Name -like "*AppFramework*") {
        $newName = $_.Name -replace "AppFramework", "AppLibs"
        Rename-Item -Path $_.FullName -NewName $newName
    }
}

Write-Host "Renaming completed!"

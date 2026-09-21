# Publishes the Classic Avalonia UI's browser page and zips it as the page every head's web host
# serves: build/VpnHood.AppUi.Presentation.Classic.Avalonia.Browser.targets places bin/avalonia-browser.zip
# beside each head as assets/web-root.zip, when it exists. In CI the publish_app workflow runs this
# once (build-browser) and hands the zip to every head build as an artifact. Runs under the .NET 10
# SDK named by global.json in this folder, with its wasm-tools workload: SkiaSharp's WebAssembly
# libraries are built for its Emscripten, which the .NET 11 preview's cannot link.
$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    # Emptied first: publish writes over the folder without clearing it, and every assembly carries a
    # hash of its content in its name, so a changed one lands beside its last version instead of
    # replacing it. The loader asks for the names its config holds and never sees the leftovers -
    # they would only ride along in the zip, a second copy of everything that changed.
    $publishDir = Join-Path $PSScriptRoot "bin/Release/net10.0-browser/publish"
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    dotnet publish VpnHood.AppUi.Presentation.Classic.Avalonia.Browser.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw "The browser publish failed." }

    $wwwroot = (Resolve-Path "bin/Release/net10.0-browser/publish/wwwroot").Path
    $zipPath = Join-Path $PSScriptRoot "bin/avalonia-browser.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath }

    # The loader asks for the plain names, so the .br and .gz twins the SDK writes beside every file
    # would only double the zip; the symbol and map files serve a debugger, not a phone.
    $files = Get-ChildItem $wwwroot -Recurse -File | Where-Object { $_.Extension -notin ".br", ".gz", ".pdb", ".map" }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files) {
            $entryName = $file.FullName.Substring($wwwroot.Length + 1).Replace("\", "/")
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName,
                [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $archive.Dispose()
    }

    $raw = ($files | Measure-Object -Property Length -Sum).Sum
    Write-Host ("avalonia-browser.zip: {0:N1} MB zipped, {1:N1} MB extracted, {2} files" -f ((Get-Item $zipPath).Length / 1MB), ($raw / 1MB), $files.Count)
}
finally {
    Pop-Location
}

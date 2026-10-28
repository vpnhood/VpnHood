# Publishes the Classic Avalonia UI's browser page and zips it as the page every head's web host
# serves, into the UI's own package: VpnHood.AppUi.Presentation.Classic.Avalonia's
# buildTransitive/web-root.zip, which its targets place beside each head as assets/web-root.zip. In CI
# the publish_app workflow runs this once (build-browser) and hands the zip to every head build as an
# artifact, and publish_nugets runs it before the pack. Runs under the .NET 10 SDK named by global.json
# in this folder, with its wasm-tools workload: SkiaSharp's WebAssembly libraries are built for its
# Emscripten, which .NET 11's cannot link.
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
    $zipPath = Join-Path $PSScriptRoot "../VpnHood.AppUi.Presentation.Classic.Avalonia/buildTransitive/web-root.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath }

    # Each file goes as the gzip twin the SDK writes beside it, name.gz, which the host sends as it
    # lies with Content-Encoding: gzip - about a third of the bytes. Not the .br twin: browsers take
    # brotli only over HTTPS, and the host is plain HTTP. index.html goes plain, as the host writes the
    # app's name, logo and colours into it. The symbol and map files serve a debugger, not a phone.
    $files = Get-ChildItem $wwwroot -Recurse -File | Where-Object { $_.Extension -notin ".br", ".gz", ".pdb", ".map" }

    # In front of the zip goes its stamp (ZipStamp in VpnHood.Net.Toolkit), as the UI store's
    # _zip-assets.ps1 writes it: "VHSTAMP:", 16 hex digits, "\n" - the first 16 hex digits of the
    # SHA-256 of the whole file, taken while the digits were still zero - so a start reads those 25
    # bytes of the zip and no more. The zip goes right after it into the same file, so its offsets
    # count the stamp and every zip tool still opens it. The file written is checked as an app reads it.
    function Get-Stamp($stream) {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try { return -join ($sha.ComputeHash($stream)[0..7] | ForEach-Object { $_.ToString("x2") }) }
        finally { $sha.Dispose() }
    }

    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $magic = [System.Text.Encoding]::ASCII.GetBytes("VHSTAMP:")
    $zipFile = [System.IO.File]::Create($zipPath)
    try {
        # the stamp's space, its digits zero until the file is hashed
        $zipFile.Write($magic, 0, $magic.Length)
        $zipFile.Write([byte[]]::new(16), 0, 16)
        $zipFile.WriteByte(10)

        $archive = [System.IO.Compression.ZipArchive]::new($zipFile, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($file in $files) {
                $entryName = $file.FullName.Substring($wwwroot.Length + 1).Replace("\", "/")
                $twin = "$($file.FullName).gz"
                if ($entryName -ne "index.html" -and (Test-Path $twin)) {
                    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $twin, "$entryName.gz",
                        [System.IO.Compression.CompressionLevel]::NoCompression) | Out-Null
                }
                else {
                    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName,
                        [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
                }
            }
        }
        finally {
            $archive.Dispose()
        }

        $zipFile.Position = 0
        $stamp = Get-Stamp $zipFile
        $zipFile.Position = $magic.Length
        $zipFile.Write([System.Text.Encoding]::ASCII.GetBytes($stamp), 0, 16)
    }
    finally {
        $zipFile.Dispose()
    }

    # as an app reads it: the stamp in front, the file's hash with its digits zero, and the zip behind it
    $bytes = [System.IO.File]::ReadAllBytes($zipPath)
    $head = [System.Text.Encoding]::ASCII.GetString($bytes, 0, 25)
    [Array]::Clear($bytes, $magic.Length, 16)
    if ($head -ne "VHSTAMP:$stamp`n" -or (Get-Stamp ([System.IO.MemoryStream]::new($bytes))) -ne $stamp) {
        throw "web-root.zip does not begin with its stamp, $stamp."
    }
    $opened = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try { $count = $opened.Entries.Count } finally { $opened.Dispose() }
    if ($count -ne @($files).Count) { throw "web-root.zip opens with $count files rather than $(@($files).Count)." }

    $raw = ($files | Measure-Object -Property Length -Sum).Sum
    Write-Host ("web-root.zip: {0:N1} MB zipped, {1:N1} MB of files, {2} files, stamp {3}" -f ((Get-Item $zipPath).Length / 1MB), ($raw / 1MB), $files.Count, $stamp)
}
finally {
    Pop-Location
}

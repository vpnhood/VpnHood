# Tells the web UI what this UI needs from the assets folder, so its build can check it and cut the
# icon font to fit: writes build/native-ui-icons.txt (the icons Mdi.cs names, as @mdi/font knows
# them) and build/native-ui-images.txt (the images this UI asks for by name).
#
# Both files are read by the SPA build: a name in them that is not a file fails that build, and the
# icon font in the assets folder is subset to the SPA's own icons plus the listed ones. So an icon
# or an image added here reaches the folder only after this script has run and the SPA is rebuilt -
# without that, the glyph is a blank box and the image throws when its page opens.
#
# The code points live in Mdi.cs (VpnHood.AppUi.Common) and the names in @mdi/font because that
# is where each is used: a
# C# page names a glyph by code point, a Vue page by name. @mdi/font's stylesheet is the one map
# between them.

$ErrorActionPreference = "Stop";

# This repo's root is the folder holding VpnHood.slnx, and <Vh> - where VpnHood and
# VpnHood.AppUi.Spa sit side by side - is its parent. Found, not counted, so moving this
# project does not silently point the lookup at the wrong folder.
$projectDir = Split-Path -Parent $PSScriptRoot;
$repoDir = $PSScriptRoot;
while ($repoDir -and -not (Test-Path (Join-Path $repoDir "VpnHood.slnx"))) { $repoDir = Split-Path -Parent $repoDir; }
if (-not $repoDir) { throw "Could not find VpnHood.slnx above $PSScriptRoot"; }
$vhFolder = Split-Path -Parent $repoDir;
$webUiDir = Join-Path $vhFolder "VpnHood.AppUi.Spa\src\VpnHood.AppUi.Presentation.Classic.Spa";
$stylesheet = Join-Path $webUiDir "node_modules\@mdi\font\css\materialdesignicons.css";
$iconsFile = Join-Path $webUiDir "build\native-ui-icons.txt";
$imagesFile = Join-Path $webUiDir "build\native-ui-images.txt";
$mdiFile = Join-Path $repoDir "src\AppUi\VpnHood.AppUi.Common\Mdi.cs";

if (!(Test-Path $stylesheet)) { throw "@mdi/font is not installed in the web UI. Run 'npm ci' in $webUiDir. ($stylesheet)"; }

$utf8 = New-Object System.Text.UTF8Encoding($false);

# ---- the icons: name by code point, as @mdi/font assigns them ----
$css = [System.IO.File]::ReadAllText($stylesheet, $utf8);
$byCodePoint = @{};
foreach ($match in [regex]::Matches($css, '\.mdi-([a-z0-9-]+)::before\s*\{\s*content:\s*"\\([0-9A-F]+)"')) {
    $codePoint = [Convert]::ToInt32($match.Groups[2].Value, 16);
    if (!$byCodePoint.ContainsKey($codePoint)) { $byCodePoint[$codePoint] = $match.Groups[1].Value; }
}
if ($byCodePoint.Count -eq 0) { throw "No icon was read from $stylesheet; has @mdi/font changed its stylesheet?"; }

$source = [System.IO.File]::ReadAllText($mdiFile, $utf8);
$icons = @();
foreach ($match in [regex]::Matches($source, 'public const string (\w+) = "\\U([0-9A-F]{8})";')) {
    $codePoint = [Convert]::ToInt32($match.Groups[2].Value, 16);
    if (!$byCodePoint.ContainsKey($codePoint)) { throw "Mdi.$($match.Groups[1].Value) (U+$($match.Groups[2].Value)) is not an icon of @mdi/font."; }
    $icons += $byCodePoint[$codePoint];
}
$icons = $icons | Sort-Object -Unique;

$iconsHeader = @"
# Icons the native (Avalonia) UI draws, which the web UI's own pages may not: the code points of
# Mdi.cs in VpnHood.AppUi.Common, as @mdi/font names. build/icon-font-plugin.ts subsets the
# icon font of the assets folder to the web UI's icons plus these, so both UIs draw from one file.
# Written by _sync-native-assets.ps1 in that project - do not edit by hand.
"@;
[System.IO.File]::WriteAllText($iconsFile, $iconsHeader + "`n" + ($icons -join "`n") + "`n", $utf8);
Write-Host "Wrote $($icons.Count) icon names to $iconsFile.";

# ---- the images: every file name the UI asks the assets folder for ----
# Both forms the UI writes are a quoted file name: ui:AppImage.Source="images/name.webp" in XAML,
# and the string a page hands to AppAssets, with or without the store's images/ prefix. Flags are
# not here - they are named by country code, never by file - and neither is a name built at run
# time from the theme (future-apps-{UiTheme}.png), which this cannot see.
$images = @();
foreach ($file in Get-ChildItem $projectDir -Recurse -Include *.cs, *.axaml) {
    $text = [System.IO.File]::ReadAllText($file.FullName, $utf8);
    foreach ($match in [regex]::Matches($text, '"(?:images/)?([\w.-]+\.(?:webp|png|svg|mp4))"')) { $images += $match.Groups[1].Value; }
}
# The apps' names too, by the store path they write (LogoAssetPath in each product's options builder): the
# file an app names must be in the store as much as one a page names. Only the prefixed form - a bare name
# in a head is not a store path - and never the build's own output.
foreach ($file in Get-ChildItem (Join-Path $repoDir "src\Apps") -Recurse -Include *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }) {
    $text = [System.IO.File]::ReadAllText($file.FullName, $utf8);
    foreach ($match in [regex]::Matches($text, '"images/([\w.-]+\.(?:webp|png|svg|mp4))"')) { $images += $match.Groups[1].Value; }
}
$images = $images | Sort-Object -Unique;

$imagesHeader = @"
# Images the native (Avalonia) UI asks the assets folder for, by name: every "{res:AppImage x}" in
# its XAML and every image name in its C#. The SPA build fails when one of these is not a file in
# src/assets/images, so a rename on either side is caught there rather than on a device.
# Written by _sync-native-assets.ps1 in that project - do not edit by hand.
"@;
[System.IO.File]::WriteAllText($imagesFile, $imagesHeader + "`n" + ($images -join "`n") + "`n", $utf8);
Write-Host "Wrote $($images.Count) image names to $imagesFile.";

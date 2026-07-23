using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Toolkit.Graphics;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.SpaWebView;

// Builds an AppResources from a SPA zip. Native branding (window/tray colors + system-tray icons)
// is read from a manifest baked into the zip at build time (branding/<theme>/manifest.json — see
// VpnHood.Client.WebUI), so the SPA package is the single source of the whole visual identity: a
// rebranded SPA rebrands the native chrome too, with no .NET change.
//
// Synchronous and "ready at first": the zip bytes are already in hand, and this reads only the
// manifest + a few small icon entries from the archive — it never extracts the 15 MB payload.
//
// 'default' is used unless a theme is named; a missing named theme falls back to 'default'. The
// manifest is produced by our own build, so a missing/invalid one is a build bug and fails loudly.
public static class SpaResourcesFactory
{
    private const string DefaultTheme = "default";

    // Overload for the usual delivery: spa.zip is embedded in the app's own assembly (in production by
    // the VpnHood.AppLib.Assets.ClassicSpa package's build targets, locally by the use-local-spa embed).
    // The caller passes its OWN assembly because that is where the zip lives — not this one.
    public static AppResources FromSpaZip(Assembly assembly, string resourceName, string theme = DefaultTheme)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"The embedded SPA bundle '{resourceName}' was not found in {assembly.GetName().Name}. " +
                "The VpnHood.AppLib.Assets.ClassicSpa package (production) or the use-local-spa embed " +
                "(local) did not supply spa.zip.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return FromSpaZip(ms.ToArray(), theme);
    }

    public static AppResources FromSpaZip(byte[] spaZipData, string theme = DefaultTheme)
    {
        var resources = new AppResources { SpaZipData = spaZipData };

        using var zip = new ZipArchive(new MemoryStream(spaZipData), ZipArchiveMode.Read);

        var folder = $"branding/{theme}";
        if (zip.GetEntry($"{folder}/manifest.json") == null && theme != DefaultTheme)
            folder = $"branding/{DefaultTheme}"; // requested theme absent -> fall back to default

        var manifestEntry = zip.GetEntry($"{folder}/manifest.json")
            ?? throw new InvalidOperationException(
                $"SPA branding manifest 'branding/{theme}/manifest.json' (and the '{DefaultTheme}' fallback) " +
                "was not found in the SPA zip. The SPA build is missing its branding output.");

        var manifest = ReadJson<SpaBrandingManifest>(manifestEntry);
        if (manifest.Colors != null) {
            resources.Colors = new AppResources.AppColors {
                WindowBackgroundColor = ParseColor(manifest.Colors.WindowBackground),
                NavigationBarColor = ParseColor(manifest.Colors.NavigationBar),
                ProgressBarColor = ParseColor(manifest.Colors.ProgressBar)
            };
        }
        else {
            VhLogger.Instance.LogWarning("SPA branding manifest has no colors section; native chrome will use defaults.");
        }

        // System-tray icons come from the theme folder; badge icons keep the Abstractions defaults.
        if (manifest.Icons is { } icons) {
            if (icons.SystemTrayConnected != null)
                resources.Icons.SystemTrayConnectedIconData = ReadIcon(zip, folder, icons.SystemTrayConnected);
            if (icons.SystemTrayConnecting != null)
                resources.Icons.SystemTrayConnectingIconData = ReadIcon(zip, folder, icons.SystemTrayConnecting);
            if (icons.SystemTrayDisconnected != null)
                resources.Icons.SystemTrayDisconnectedIconData = ReadIcon(zip, folder, icons.SystemTrayDisconnected);
        }

        return resources;
    }

    private static VhColor? ParseColor(string? hex) =>
        string.IsNullOrWhiteSpace(hex) ? null : VhColor.Parse(hex!);

    private static byte[] ReadIcon(ZipArchive zip, string folder, string fileName)
    {
        var entry = zip.GetEntry($"{folder}/{fileName}")
            ?? throw new InvalidOperationException($"SPA branding icon '{folder}/{fileName}' is listed in the manifest but missing from the zip.");
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static T ReadJson<T>(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"SPA branding: '{entry.FullName}' deserialized to null.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class SpaBrandingManifest
    {
        public int SchemaVersion { get; set; }
        public SpaBrandingColors? Colors { get; set; }
        public SpaBrandingIcons? Icons { get; set; }
    }

    private sealed class SpaBrandingColors
    {
        public string? WindowBackground { get; set; }
        public string? NavigationBar { get; set; }
        public string? ProgressBar { get; set; }
    }

    private sealed class SpaBrandingIcons
    {
        public string? SystemTrayConnected { get; set; }
        public string? SystemTrayConnecting { get; set; }
        public string? SystemTrayDisconnected { get; set; }
    }
}

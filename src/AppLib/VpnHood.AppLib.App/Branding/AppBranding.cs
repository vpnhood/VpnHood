using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Graphics;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Streams;

namespace VpnHood.AppLib.App.Branding;

// The look the OS chrome draws with - the window and bar colours, the tray icons - as the UI's
// store carries it: branding/<theme>/manifest.json and the icon files it names, written by the
// UI's own build. So the store is the single source of the whole visual identity, and a rebranded
// store rebrands the native chrome with no .NET change.
//
// Read through the provider, from whatever the store is on this platform, and applied onto the
// resources the app already holds - only what the manifest names changes. A theme the store does
// not have, or a manifest that names an icon the store does not carry, is a build bug and throws.
public static class AppBranding
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // For the app's start: the branding applied, or - no store, no such theme, a store that could
    // not be read - an error in the log and the built-in look, not a dead app. Never faulted, so
    // whatever waits on it (VpnHoodApp.ResourcesLoaded) needs no catch.
    public static async Task LoadAsync(AppResources resources, IAssetProvider? assets, string theme)
    {
        if (assets is null)
            return;

        try {
            await ApplyAsync(resources, assets, theme, CancellationToken.None).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not read the app's branding from the UI's store.");
        }
    }

    public static async Task ApplyAsync(AppResources resources, IAssetProvider assets, string theme,
        CancellationToken cancellationToken)
    {
        var folder = $"branding/{theme}";
        var manifest = await TryReadManifest(assets, folder, cancellationToken).Vhc()
                       ?? throw new InvalidOperationException(
                           $"The UI's store has no '{theme}' branding: there is no '{folder}/manifest.json' in it.");

        if (manifest.Colors is { } colors) {
            resources.Colors = new AppResources.AppColors {
                WindowBackgroundColor = ParseColor(colors.WindowBackground),
                NavigationBarColor = ParseColor(colors.NavigationBar),
                ProgressBarColor = ParseColor(colors.ProgressBar)
            };
        }
        else {
            VhLogger.Instance.LogWarning("The branding manifest names no colours; the native chrome keeps its defaults.");
        }

        // the tray icons; the badge icons keep the library's own
        if (manifest.Icons is { } icons) {
            if (icons.SystemTrayConnected != null)
                resources.Icons.SystemTrayConnectedIconData = await ReadIcon(assets, folder, icons.SystemTrayConnected, cancellationToken).Vhc();
            if (icons.SystemTrayConnecting != null)
                resources.Icons.SystemTrayConnectingIconData = await ReadIcon(assets, folder, icons.SystemTrayConnecting, cancellationToken).Vhc();
            if (icons.SystemTrayDisconnected != null)
                resources.Icons.SystemTrayDisconnectedIconData = await ReadIcon(assets, folder, icons.SystemTrayDisconnected, cancellationToken).Vhc();
        }
    }

    private static async Task<AppBrandingManifest?> TryReadManifest(IAssetProvider assets, string folder,
        CancellationToken cancellationToken)
    {
        await using var stream = await assets.TryOpenReadAsync($"{folder}/manifest.json", cancellationToken).Vhc();
        if (stream is null)
            return null;

        return await JsonSerializer.DeserializeAsync<AppBrandingManifest>(stream, JsonOptions, cancellationToken).Vhc()
               ?? throw new InvalidOperationException($"The branding manifest '{folder}/manifest.json' is empty.");
    }

    private static async Task<ReadOnlyMemory<byte>> ReadIcon(IAssetProvider assets, string folder, string fileName,
        CancellationToken cancellationToken)
    {
        await using var stream = await assets.OpenReadAsync($"{folder}/{fileName}", cancellationToken).Vhc();
        var memoryStream = await stream.ToMemoryStreamAsync(cancellationToken).Vhc();
        return memoryStream.ToArray();
    }

    private static VhColor? ParseColor(string? hex)
    {
        return VhColor.TryParse(hex, out var color) ? color : null;
    }
}

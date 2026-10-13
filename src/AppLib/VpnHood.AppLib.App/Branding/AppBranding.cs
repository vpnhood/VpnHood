using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Graphics;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppLib.App.Branding;

// The colours the OS chrome draws with - the window's, the bars' - as the UI's store carries them:
// branding/<theme>/manifest.json, written by the UI's own build. So a rebranded store recolours the
// native chrome with no .NET change. The tray's pictures are not the store's: the tray draws the
// app's own icon, from its build.
//
// Read through the provider, from whatever the store is on this platform, and applied onto the
// resources the app already holds - only what the manifest names changes. A theme the store does
// not have is a build bug and throws.
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

    private static VhColor? ParseColor(string? hex)
    {
        return VhColor.TryParse(hex, out var color) ? color : null;
    }
}

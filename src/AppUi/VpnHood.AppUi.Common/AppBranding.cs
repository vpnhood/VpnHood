using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Graphics;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Common;

// The colours the native chrome around the UI draws with - the window's, the bars' - as the theme
// gives them in the UI's store: branding/<theme>/manifest.json. The theme's, not the app's, which only
// names a theme (UiTheme); so a rebranded store recolours the chrome with no .NET change.
//
// A theme the store does not have is a build bug: ReadAsync throws, and LoadAsync, for a start, logs
// it and answers the built-in look.
public static class AppBranding
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // For a start: the branding, or - no store, no such theme, a store that could not be read - an
    // error in the log and the built-in look, not a dead app. Never faulted, so whatever waits on it
    // needs no catch.
    public static async Task<AppBrandingColors> LoadAsync(IAssetProvider? assets, string theme)
    {
        if (assets is null)
            return new AppBrandingColors();

        try {
            return await ReadAsync(assets, theme, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not read the app's branding from the UI's store.");
            return new AppBrandingColors();
        }
    }

    public static async Task<AppBrandingColors> ReadAsync(IAssetProvider assets, string theme,
        CancellationToken cancellationToken)
    {
        var folder = $"branding/{theme}";
        var manifest = await TryReadManifest(assets, folder, cancellationToken).ConfigureAwait(false)
                       ?? throw new InvalidOperationException(
                           $"The UI's store has no '{theme}' branding: there is no '{folder}/manifest.json' in it.");

        if (manifest.Colors is not { } colors) {
            VhLogger.Instance.LogWarning("The branding manifest names no colours; the native chrome keeps its defaults.");
            return new AppBrandingColors();
        }

        return new AppBrandingColors {
            WindowBackgroundColor = ParseColor(colors.WindowBackground),
            NavigationBarColor = ParseColor(colors.NavigationBar),
            ProgressBarColor = ParseColor(colors.ProgressBar)
        };
    }

    private static async Task<AppBrandingManifest?> TryReadManifest(IAssetProvider assets, string folder,
        CancellationToken cancellationToken)
    {
        await using var stream = await assets.TryOpenReadAsync($"{folder}/manifest.json", cancellationToken).ConfigureAwait(false);
        if (stream is null)
            return null;

        return await JsonSerializer.DeserializeAsync<AppBrandingManifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException($"The branding manifest '{folder}/manifest.json' is empty.");
    }

    private static VhColor? ParseColor(string? hex)
    {
        return VhColor.TryParse(hex, out var color) ? color : null;
    }
}

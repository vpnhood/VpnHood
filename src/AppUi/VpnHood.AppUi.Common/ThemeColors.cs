using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Graphics;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Common;

// The colours the native chrome around the UI draws with - the window's, the bars' - as the theme
// gives them in the UI's store: themes/<theme>/manifest.json. The theme's, not the app's, which only
// names a theme (UiTheme); so the theme's designer recolours the chrome in the store, with no .NET
// change. A colour the manifest does not name is null, and that part of the chrome keeps the system's.
//
// A theme the store does not have is a build bug: ReadAsync throws, and LoadAsync, for a start, logs
// it and answers the built-in look.
public class ThemeColors
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public VhColor? WindowBackgroundColor { get; init; }
    public VhColor? NavigationBarColor { get; init; }
    public VhColor? ProgressBarColor { get; init; }

    // For a start: the theme's colours, or - no store, no such theme, a store that could not be read -
    // an error in the log and the built-in look, not a dead app. Never faulted but by a cancel, so whatever
    // waits on it needs no catch.
    public static async Task<ThemeColors> LoadAsync(IAssetProvider? assets, string theme,
        CancellationToken cancellationToken)
    {
        if (assets is null)
            return new ThemeColors();

        try {
            return await ReadAsync(assets, theme, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not read the theme's colours from the UI's store.");
            return new ThemeColors();
        }
    }

    public static async Task<ThemeColors> ReadAsync(IAssetProvider assets, string theme,
        CancellationToken cancellationToken)
    {
        var folder = $"themes/{theme}";
        var manifest = await TryReadManifest(assets, folder, cancellationToken).ConfigureAwait(false)
                       ?? throw new InvalidOperationException(
                           $"The UI's store has no '{theme}' theme: there is no '{folder}/manifest.json' in it.");

        if (manifest.Colors is not { } colors) {
            VhLogger.Instance.LogWarning("The theme's manifest names no colours; the native chrome keeps its defaults.");
            return new ThemeColors();
        }

        return new ThemeColors {
            WindowBackgroundColor = ParseColor(colors.WindowBackground),
            NavigationBarColor = ParseColor(colors.NavigationBar),
            ProgressBarColor = ParseColor(colors.ProgressBar)
        };
    }

    private static async Task<ThemeManifest?> TryReadManifest(IAssetProvider assets, string folder,
        CancellationToken cancellationToken)
    {
        await using var stream = await assets.TryOpenReadAsync($"{folder}/manifest.json", cancellationToken).ConfigureAwait(false);
        if (stream is null)
            return null;

        return await JsonSerializer.DeserializeAsync<ThemeManifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException($"The theme's manifest '{folder}/manifest.json' is empty.");
    }

    private static VhColor? ParseColor(string? hex)
    {
        return VhColor.TryParse(hex, out var color) ? color : null;
    }
}

using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace VpnHood.AppLib.AvaloniaUI.Resources;

// The web UI's country flags (Assets/Flags, one PNG per ISO code), decoded once each. Null for
// the automatic location and for a code without a flag, so a binding shows nothing rather than
// a broken image.
public static class Flags
{
    private static readonly Dictionary<string, Bitmap?> Cache = new();

    public static Bitmap? Get(string? countryCode)
    {
        if (string.IsNullOrEmpty(countryCode) || countryCode == "*")
            return null;

        var key = countryCode.ToLowerInvariant();
        lock (Cache) {
            if (Cache.TryGetValue(key, out var cached))
                return cached;
        }

        var uri = new Uri($"avares://VpnHood.AppLib.AvaloniaUI/Assets/Flags/{key}.png");
        Bitmap? bitmap = null;
        if (AssetLoader.Exists(uri)) {
            using var stream = AssetLoader.Open(uri);
            bitmap = new Bitmap(stream);
        }

        lock (Cache) {
            Cache[key] = bitmap;
        }
        return bitmap;
    }
}

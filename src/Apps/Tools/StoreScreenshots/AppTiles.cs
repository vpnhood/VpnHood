using System.Text.Json.Nodes;
using SkiaSharp;
using VpnHood.AppUi.Common;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Streams;

namespace VpnHood.App.StoreScreenshots;

// The icons the Split Apps screen shows, drawn here rather than taken from anywhere.
//
// On a device that page lists what is installed, icons and all. A store picture cannot show those:
// another company's icon is its artwork and its trademark, and our listing is not the place for
// either. A tile in the colour the app is known by, with its initial in the UI's own face, reads as
// a phone's app list and borrows nothing. Drawn from fixed colours and a font out of the asset
// store, so every machine draws the same tiles.
internal static class AppTiles
{
    private const int Size = 96;

    public static async Task<JsonArray> DrawAsync(IReadOnlyList<DemoApp> apps, IAssetProvider assets,
        CancellationToken cancellationToken)
    {
        if (apps.Count == 0)
            return [];

        using var typeface = await LoadTypefaceAsync(assets, cancellationToken).Vhc();
        var drawn = new JsonArray();
        foreach (var app in apps)
            drawn.Add(new JsonObject {
                ["appId"] = app.AppId,
                ["appName"] = app.AppName,
                // the UI puts the data: prefix on it itself, so only the payload goes here
                ["iconPng"] = Convert.ToBase64String(Draw(app, typeface))
            });

        return drawn;
    }

    private static byte[] Draw(DemoApp app, SKTypeface typeface)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);

        // the list clips its avatars to a circle itself, so the tile is drawn square
        using var background = new SKPaint { Shader = Shader(app.Background), IsAntialias = true };
        canvas.DrawRect(new SKRect(0, 0, Size, Size), background);

        using var font = new SKFont(typeface, Size * 0.52f);
        using var foreground = new SKPaint { Shader = Shader(app.Foreground), IsAntialias = true };
        var letter = app.AppName[..1].ToUpperInvariant();
        var width = font.MeasureText(letter, out var bounds);
        canvas.DrawText(letter, (Size - width) / 2, Size / 2f - bounds.MidY, font, foreground);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKShader Shader(IReadOnlyList<string> colors)
    {
        var parsed = colors.Select(SKColor.Parse).ToArray();
        return parsed.Length == 1
            ? SKShader.CreateColor(parsed[0])
            : SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(Size, Size), parsed, SKShaderTileMode.Clamp);
    }

    // The face the UI writes with, out of the store it draws from: the tiles then match the
    // screenshot they sit in, on any machine.
    private static async Task<SKTypeface> LoadTypefaceAsync(IAssetProvider assets, CancellationToken cancellationToken)
    {
        var names = await AssetIndex.ReadAsync(assets, AppFonts.IndexPath, cancellationToken).Vhc();
        var name = names.FirstOrDefault(x => x.Contains("Poppins", StringComparison.OrdinalIgnoreCase)
                                             && x.Contains("Bold", StringComparison.OrdinalIgnoreCase))
                   ?? names.FirstOrDefault(x => x.Contains("Poppins", StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException(
                       $"The asset store lists no Poppins face to draw the app tiles with ({AppFonts.IndexPath}).");

        await using var stream = await assets.OpenReadAsync($"{AppFonts.FolderName}/{name}", cancellationToken).Vhc();
        var memory = await stream.ToMemoryStreamAsync(cancellationToken).Vhc();
        return SKTypeface.FromStream(memory)
               ?? throw new InvalidOperationException($"{name} is not a font Skia can read.");
    }
}

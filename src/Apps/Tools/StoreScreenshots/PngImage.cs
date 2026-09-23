using Avalonia.Media;
using SkiaSharp;

namespace VpnHood.App.StoreScreenshots;

// The picture files, through the same rasterizer that drew them.
//
// Every PNG written here is packed as hard as the format allows: a store-asset repo has to stay
// clonable - an F-Droid or IzzyOnDroid maintainer clones it to read our listing - and a screenshot
// set is the bulk of it. Nothing is ever resampled on the way out except the one deliberate
// downsample below: a store screenshot must be the pixels we drew, not an encoder's opinion of
// them.
internal static class PngImage
{
    // Mitchell over a plain box filter: the same deliberate 2:1 reduction of an already-rasterized
    // picture the web UI's engine did with a canvas, and the reason the frames are drawn above
    // size in the first place - text drawn straight at the output scale reads soft.
    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

    public static SKBitmap Load(string path)
    {
        return SKBitmap.Decode(path) ?? throw new InvalidOperationException($"{path} is not a picture this can read.");
    }

    // The colour a band continues: the middle of the capture's own top or bottom row.
    public static Color EdgeColor(SKBitmap bitmap, bool top)
    {
        var pixel = bitmap.GetPixel(bitmap.Width / 2, top ? 1 : bitmap.Height - 2);
        return Color.FromRgb(pixel.Red, pixel.Green, pixel.Blue);
    }

    public static SKBitmap Resize(SKBitmap source, int width, int height)
    {
        var resized = source.Resize(new SKImageInfo(width, height, source.ColorType, source.AlphaType), Sampling);
        return resized ?? throw new InvalidOperationException($"Could not resize a picture to {width}x{height}.");
    }

    // Scale-crops instead of stretching, anchored at the top the way the mockups show a capture:
    // for a source whose shape is not the destination's (a still picture used as a shot).
    public static SKBitmap Cover(SKBitmap source, int width, int height)
    {
        var scale = Math.Max((double)width / source.Width, (double)height / source.Height);
        var scaled = Resize(source, (int)Math.Ceiling(source.Width * scale), (int)Math.Ceiling(source.Height * scale));
        if (scaled.Width == width && scaled.Height == height)
            return scaled;

        using (scaled) {
            var cropped = new SKBitmap(new SKImageInfo(width, height, source.ColorType, source.AlphaType));
            using var canvas = new SKCanvas(cropped);
            canvas.DrawBitmap(scaled, (width - scaled.Width) / 2f, 0);
            return cropped;
        }
    }

    public static void Save(SKBitmap bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)
                                  ?? throw new InvalidOperationException($"There is no folder in the path {path}."));
        using var pixmap = bitmap.PeekPixels()
                           ?? throw new InvalidOperationException("The drawn picture has no pixels to write.");
        using var stream = new SKFileWStream(path);
        // every filter tried per row, and deflate at its slowest: a screenshot is written once and
        // cloned forever
        var options = new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 9);
        if (!pixmap.Encode(stream, options))
            throw new InvalidOperationException($"Could not write {path}.");
    }
}

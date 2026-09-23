using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace VpnHood.App.StoreScreenshots;

// A capture, dressed and written at the size its store takes.
//
// Drawn above size and reduced once, deliberately: a mockup drawn straight at the output scale has
// visibly soft text, because the capture inside it is being shrunk as it is drawn. Two passes -
// draw at twice the size, then one high-quality reduction - is what the web UI's engine did with a
// browser and a canvas, and this keeps it.
internal static class FrameRenderer
{
    private const int Supersample = 2;

    public static (int Width, int Height) Render(DeviceSpec device, string capturePath, string outputPath)
    {
        using var capture = PngImage.Load(capturePath);
        var (width, height) = device.FinalSize;

        // no dress: the capture itself at the store's size, the whole panel (a television, Google Play)
        if (device.Frame == FrameKind.None) {
            using var bare = PngImage.Cover(capture, width, height);
            PngImage.Save(bare, outputPath);
            return (width, height);
        }

        using var bitmap = new Bitmap(capturePath);
        var control = device.Frame == FrameKind.Desktop
            ? DesktopFrame.Build(device, bitmap, PngImage.EdgeColor(capture, top: true))
            : PhoneFrame.Build(device, bitmap, PngImage.EdgeColor(capture, top: true), PngImage.EdgeColor(capture, top: false));

        var scene = device.Frame == FrameKind.Desktop
            ? new Size(device.Canvas!.Width, device.Canvas.Height)
            : new Size(device.CssWidth, device.CssHeight);

        // the scene is laid out in logical pixels; the store's file is that scene at the device's
        // pixel ratio, and it is drawn at twice that before the one reduction below
        using var drawn = Draw(control, scene, width / scene.Width * Supersample);
        using var final = PngImage.Resize(drawn, width, height);
        PngImage.Save(final, outputPath);
        return (width, height);
    }

    // The scene in a window of its own size, drawn at a multiple of it and read back.
    //
    // A window rather than a render target: a render target drawn into from a tree that belongs to
    // no window paints the panels and nothing else - no text, no pictures, no shadows - and a
    // half-drawn mockup is worse than none. The headless window is the same machinery the capture
    // uses, and the only display it needs is memory.
    private static SKBitmap Draw(Control control, Size scene, double scale)
    {
        var window = new Window {
            MinWidth = 0,
            MinHeight = 0,
            Width = scene.Width,
            Height = scene.Height,
            Content = control
        };
        window.Show();
        window.SetRenderScaling(scale);

        // no dispatcher loop runs here: the layout pass and the frame are asked for by hand
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        DumpLayout(control);

        using var frame = window.CaptureRenderedFrame()
                          ?? throw new InvalidOperationException("The window has drawn no frame of the mockup.");
        var bitmap = ToSkia(frame);
        window.Close();
        return bitmap;
    }

    // The frame's pixels as Skia holds them, for the reduction and the encoder. The order of the
    // channels is the one the frame says it has, never an assumption: read as the other order, a
    // screenshot comes out in the wrong colours and is still a perfectly valid picture.
    private static SKBitmap ToSkia(WriteableBitmap frame)
    {
        using var buffer = frame.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);

        var info = new SKImageInfo(buffer.Size.Width, buffer.Size.Height, ColorType(buffer.Format), AlphaType(frame.AlphaFormat));
        var bitmap = new SKBitmap(info);
        Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
        return bitmap;
    }

    private static SKColorType ColorType(PixelFormat format)
    {
        if (format == PixelFormats.Bgra8888)
            return SKColorType.Bgra8888;
        if (format == PixelFormats.Rgba8888)
            return SKColorType.Rgba8888;
        throw new NotSupportedException($"A frame in {format} is not a format this writes.");
    }

    private static SKAlphaType AlphaType(AlphaFormat? format)
    {
        return format switch {
            AlphaFormat.Premul or null => SKAlphaType.Premul,
            AlphaFormat.Unpremul => SKAlphaType.Unpremul,
            AlphaFormat.Opaque => SKAlphaType.Opaque,
            _ => throw new NotSupportedException($"A frame with {format} alpha is not a format this writes.")
        };
    }

    // What the layout made of the scene, when a frame comes out wrong: every control with the box
    // it was given. Behind a switch because it is a few hundred lines for one picture.
    private static void DumpLayout(Visual visual, int depth = 0)
    {
        if (Environment.GetEnvironmentVariable("VH_FRAME_LAYOUT") != "1")
            return;

        Console.WriteLine($"{new string(' ', depth * 2)}{visual.GetType().Name} {visual.Bounds}");
        foreach (var child in visual.GetVisualChildren())
            DumpLayout(child, depth + 1);
    }
}

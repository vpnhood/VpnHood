using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace VpnHood.AppUi.Hosting.Avalonia.Desktop;

// The tray's three pictures: the app's own icon (AvaloniaAppIcon) with the connection's state in a
// badge on its lower right - a tick, an exclamation mark, a cross - which carries no brand, so a
// fork's icon is its tray's with nothing more to supply. With no icon, the badge alone, still a
// picture a person finds. Each is an icon file of every size a tray takes at its scalings: Windows'
// picks the one nearest its own (16 px at 100%), Linux's the largest, which it scales.
internal static class AvaloniaTrayBadge
{
    private static readonly int[] Sizes = [16, 20, 24, 28, 32, 40, 48, 64];
    private static readonly Color ConnectedColor = Color.FromRgb(0x08, 0x87, 0x41);
    private static readonly Color ConnectingColor = Color.FromRgb(0xD5, 0xA4, 0x10);
    private static readonly Color DisconnectedColor = Color.FromRgb(0xDA, 0x24, 0x2A);

    public static WindowIcon Connected(IReadOnlyList<Bitmap> appIcon)
    {
        return Create(appIcon, ConnectedColor, DrawTick);
    }

    public static WindowIcon Connecting(IReadOnlyList<Bitmap> appIcon)
    {
        return Create(appIcon, ConnectingColor, DrawExclamationMark);
    }

    public static WindowIcon Disconnected(IReadOnlyList<Bitmap> appIcon)
    {
        return Create(appIcon, DisconnectedColor, DrawCross);
    }

    private static WindowIcon Create(IReadOnlyList<Bitmap> appIcon, Color color,
        Action<DrawingContext, Rect, IPen> drawMark)
    {
        var pictures = Sizes.Select(size => (size, Draw(appIcon, size, color, drawMark))).ToArray();
        return new WindowIcon(new MemoryStream(ToIconFile(pictures)));
    }

    // One size, as PNG: the icon with a gap cut around the badge, so the badge reads on any icon,
    // then the badge.
    private static byte[] Draw(IReadOnlyList<Bitmap> appIcon, int size, Color color,
        Action<DrawingContext, Rect, IPen> drawMark)
    {
        var whole = new Rect(0, 0, size, size);
        var picture = appIcon.FirstOrDefault(x => x.PixelSize.Width >= size) ?? appIcon.LastOrDefault();
        var badge = picture == null ? whole : new Rect(size * 0.4375, size * 0.4375, size * 0.5625, size * 0.5625);
        using var target = new RenderTargetBitmap(new PixelSize(size, size));
        using (var context = target.CreateDrawingContext(true)) {
            if (picture != null) {
                using var scaled = picture.CreateScaledBitmap(new PixelSize(size, size));
                var cut = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(whole),
                    new EllipseGeometry(badge.Inflate(badge.Width * 0.12)));
                using (context.PushGeometryClip(cut))
                    context.DrawImage(scaled, whole);
            }

            context.DrawEllipse(new SolidColorBrush(color), null, badge);
            drawMark(context, badge, new Pen(Brushes.White, badge.Width * 0.15, lineCap: PenLineCap.Round,
                lineJoin: PenLineJoin.Round));
        }

        using var png = new MemoryStream();
        target.Save(png, PngBitmapEncoderOptions.Default);
        return png.ToArray();
    }

    private static void DrawTick(DrawingContext context, Rect badge, IPen pen)
    {
        context.DrawGeometry(null, pen, new PolylineGeometry(
            [At(badge, 0.28, 0.52), At(badge, 0.44, 0.68), At(badge, 0.73, 0.37)], isFilled: false));
    }

    private static void DrawExclamationMark(DrawingContext context, Rect badge, IPen pen)
    {
        context.DrawLine(pen, At(badge, 0.5, 0.26), At(badge, 0.5, 0.56));
        context.DrawEllipse(pen.Brush, null, At(badge, 0.5, 0.75), pen.Thickness * 0.6, pen.Thickness * 0.6);
    }

    private static void DrawCross(DrawingContext context, Rect badge, IPen pen)
    {
        context.DrawLine(pen, At(badge, 0.33, 0.33), At(badge, 0.67, 0.67));
        context.DrawLine(pen, At(badge, 0.67, 0.33), At(badge, 0.33, 0.67));
    }

    private static Point At(Rect badge, double x, double y)
    {
        return new Point(badge.X + badge.Width * x, badge.Y + badge.Height * y);
    }

    // An icon file of PNG pictures, which Windows reads since Vista and Skia everywhere: the header,
    // an entry a picture - its size, 32 bits a pixel, its length and offset - then the pictures.
    private static byte[] ToIconFile(IReadOnlyList<(int Size, byte[] Png)> pictures)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)pictures.Count);
        var offset = 6 + 16 * pictures.Count;
        foreach (var (size, png) in pictures) {
            writer.Write((byte)size);
            writer.Write((byte)size);
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in pictures)
            writer.Write(png);

        writer.Flush();
        return stream.ToArray();
    }
}

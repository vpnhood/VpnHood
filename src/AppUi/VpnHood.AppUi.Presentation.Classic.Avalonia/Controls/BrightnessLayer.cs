using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

// CSS's filter: brightness(Factor) over whatever is drawn beneath it, up to the edge of the
// background it lies on (BackgroundEdge). A colour dodge divides each channel by one minus the grey
// over it, so a grey of 1 - 1/Factor multiplies it by Factor; Avalonia blends only bitmaps, so the
// grey is one stretched pixel. At 1 it draws nothing.
public class BrightnessLayer : Control
{
    public static readonly StyledProperty<double> FactorProperty =
        AvaloniaProperty.Register<BrightnessLayer, double>(nameof(Factor), 1);

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        Border.CornerRadiusProperty.AddOwner<BrightnessLayer>();

    public static readonly StyledProperty<Thickness> BorderThicknessProperty =
        Border.BorderThicknessProperty.AddOwner<BrightnessLayer>();

    private static readonly RenderOptions Dodge = new() { BitmapBlendingMode = BitmapBlendingMode.ColorDodge };
    private static readonly Dictionary<byte, WriteableBitmap> Greys = [];

    static BrightnessLayer()
    {
        AffectsRender<BrightnessLayer>(FactorProperty, CornerRadiusProperty, BorderThicknessProperty);
    }

    public double Factor {
        get => GetValue(FactorProperty);
        set => SetValue(FactorProperty, value);
    }

    public CornerRadius CornerRadius {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public Thickness BorderThickness {
        get => GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var edge = BackgroundEdge.Of(Bounds.Size, CornerRadius, BorderThickness);
        if (Factor <= 1 || edge.Rect.Width <= 0 || edge.Rect.Height <= 0)
            return;

        using var clip = context.PushClip(edge);
        using var blend = context.PushRenderOptions(Dodge);
        context.DrawImage(Grey((byte)Math.Round(255 * (1 - 1 / Factor))), new Rect(0, 0, 1, 1), edge.Rect);
    }

    private static WriteableBitmap Grey(byte value)
    {
        if (Greys.TryGetValue(value, out var cached))
            return cached;

        var bitmap = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        Marshal.WriteInt32(buffer.Address, (255 << 24) | (value << 16) | (value << 8) | value);
        Greys[value] = bitmap;
        return bitmap;
    }
}

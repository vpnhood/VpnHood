using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Net.Codecrete.QrCodeGenerator;

namespace VpnHood.AppLib.AvaloniaUI.Controls;

// A QR code drawn straight into the control's rectangle: white plate, black modules, a two-module
// quiet zone. Level M, the same as the web UI's plate, and no image in between - a TV's panel is
// large and a phone scans a code of a tenth of the viewing distance first time.
public class QrCodeControl : Control
{
    private const int QuietZone = 2;

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<QrCodeControl, string?>(nameof(Text));

    static QrCodeControl()
    {
        AffectsRender<QrCodeControl>(TextProperty);
    }

    public string? Text {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        context.FillRectangle(Brushes.White, new Rect(size));

        var text = Text;
        if (string.IsNullOrEmpty(text))
            return;

        var qr = QrCode.EncodeText(text, QrCode.Ecc.Medium);
        var modules = qr.Size + QuietZone * 2;
        var cell = Math.Min(size.Width, size.Height) / modules;
        var originX = (size.Width - cell * modules) / 2;
        var originY = (size.Height - cell * modules) / 2;

        // a hair of overlap between cells, or the renderer leaves seams at fractional sizes
        var cellRect = cell + 0.5;
        for (var y = 0; y < qr.Size; y++)
            for (var x = 0; x < qr.Size; x++)
                if (qr.GetModule(x, y))
                    context.FillRectangle(Brushes.Black,
                        new Rect(originX + (x + QuietZone) * cell, originY + (y + QuietZone) * cell, cellRect, cellRect));
    }
}

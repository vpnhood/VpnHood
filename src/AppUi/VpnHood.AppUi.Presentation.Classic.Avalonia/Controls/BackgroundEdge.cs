using Avalonia;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

// Where a control's background ends: halfway into its border, where Avalonia draws a background by
// default (BackgroundSizing.CenterBorder), its corners rounded to match. A layer drawn over a button
// stops there, so it neither leaves a rim of the button nor lights the page round it.
internal static class BackgroundEdge
{
    public static RoundedRect Of(Size size, CornerRadius corner, Thickness border)
    {
        var half = border * 0.5;
        return new RoundedRect(new Rect(size).Deflate(half),
            Radius(corner.TopLeft, Math.Max(half.Left, half.Top)),
            Radius(corner.TopRight, Math.Max(half.Right, half.Top)),
            Radius(corner.BottomRight, Math.Max(half.Right, half.Bottom)),
            Radius(corner.BottomLeft, Math.Max(half.Left, half.Bottom)));
    }

    private static Vector Radius(double radius, double inset)
    {
        var r = Math.Max(0, radius - inset);
        return new Vector(r, r);
    }
}

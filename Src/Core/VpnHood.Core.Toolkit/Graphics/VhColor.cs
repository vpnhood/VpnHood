// ReSharper disable NotAccessedPositionalProperty.Global

namespace VpnHood.Core.Toolkit.Graphics;

public record struct VhColor(byte R, byte G, byte B, byte A)
{
    public static VhColor FromArgb(byte a, byte r, byte g, byte b)
    {
        return new VhColor(R: r, G: g, B: b, A: a);
    }

    public static VhColor FromRgb(byte r, byte g, byte b)
    {
        return new VhColor(R: r, G: g, B: b, A: 255);
    }

    // it can be #RRGGBB or #AARRGGBB
    public static VhColor Parse(string value)
    {
        value = value.Replace("#", ""); // Remove the hash if present
        switch (value.Length)
        {
            case 6:
            {
                var r = Convert.ToByte(value[..2], 16);
                var g = Convert.ToByte(value[2..4], 16);
                var b = Convert.ToByte(value[4..6], 16);
                return FromRgb(r, g, b);
            }
            case 8:
            {
                var a = Convert.ToByte(value[..2], 16);
                var r = Convert.ToByte(value[2..4], 16);
                var g = Convert.ToByte(value[4..6], 16);
                var b = Convert.ToByte(value[6..8], 16);
                return FromArgb(a, r, g, b);
            }
            default:
                throw new FormatException("Invalid color format.");
        }
    }
}
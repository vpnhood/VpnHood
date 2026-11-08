// ReSharper disable NotAccessedPositionalProperty.Global

namespace VpnHood.Net.Toolkit.Graphics;

/// <summary>Represents a color by its red, green, blue and alpha components.</summary>
/// <param name="R">The red component.</param>
/// <param name="G">The green component.</param>
/// <param name="B">The blue component.</param>
/// <param name="A">The alpha component: 0 is transparent, 255 opaque.</param>
public record struct VhColor(byte R, byte G, byte B, byte A)
{
    /// <summary>Creates a color from its alpha, red, green and blue components.</summary>
    /// <param name="a">The alpha component: 0 is transparent, 255 opaque.</param>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <returns>The color.</returns>
    public static VhColor FromArgb(byte a, byte r, byte g, byte b)
    {
        return new VhColor(R: r, G: g, B: b, A: a);
    }

    /// <summary>Creates an opaque color from its red, green and blue components.</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <returns>The color.</returns>
    public static VhColor FromRgb(byte r, byte g, byte b)
    {
        return new VhColor(R: r, G: g, B: b, A: 255);
    }

    /// <summary>Parses a color written in hexadecimal as <c>#RRGGBB</c> or <c>#AARRGGBB</c>; the <c>#</c> may be left out.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The color.</returns>
    /// <exception cref="FormatException">The text is in neither form, or is not hexadecimal.</exception>
    /// <exception cref="ArgumentException">A component is written with a minus sign.</exception>
    public static VhColor Parse(string value)
    {
        var hex = value.Replace("#", ""); // remove the hash if present
        switch (hex.Length) {
            case 6: {
                var r = Convert.ToByte(hex[..2], 16);
                var g = Convert.ToByte(hex[2..4], 16);
                var b = Convert.ToByte(hex[4..6], 16);
                return FromRgb(r, g, b);
            }
            case 8: {
                var a = Convert.ToByte(hex[..2], 16);
                var r = Convert.ToByte(hex[2..4], 16);
                var g = Convert.ToByte(hex[4..6], 16);
                var b = Convert.ToByte(hex[6..8], 16);
                return FromArgb(a, r, g, b);
            }
            default:
                throw new FormatException($"Invalid color format: '{value}'. Expected #RRGGBB or #AARRGGBB.");
        }
    }

    /// <summary>Tries to parse a color written as <see cref="Parse"/> reads it.</summary>
    /// <param name="value">The text, or <see langword="null"/>.</param>
    /// <param name="color">The color, when parsed; otherwise, the default.</param>
    /// <returns>
    /// <see langword="true"/> if the text is a color; <see langword="false"/> if it is null, blank or malformed.
    /// </returns>
    public static bool TryParse(string? value, out VhColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;
            
        try {
            color = Parse(value);
            return true;
        }
        catch (FormatException) {
            return false;
        }
    }
}
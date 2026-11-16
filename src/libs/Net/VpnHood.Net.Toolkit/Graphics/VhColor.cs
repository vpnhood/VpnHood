// ReSharper disable NotAccessedPositionalProperty.Global

using System.Buffers;
using System.Globalization;

namespace VpnHood.Net.Toolkit.Graphics;

/// <summary>Represents a color by its red, green, blue and alpha components.</summary>
/// <param name="R">The red component.</param>
/// <param name="G">The green component.</param>
/// <param name="B">The blue component.</param>
/// <param name="A">The alpha component: 0 is transparent, 255 opaque.</param>
public record struct VhColor(byte R, byte G, byte B, byte A)
{
    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

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
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The text is in neither form, or is not hexadecimal.</exception>
    public static VhColor Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        // one hash, in front; then hexadecimal digits alone: no sign, no space, no second hash
        var hex = value.AsSpan();
        if (hex.StartsWith('#'))
            hex = hex[1..];

        if (hex.Length is not (6 or 8) || hex.ContainsAnyExcept(HexDigits))
            throw new FormatException($"Invalid color format: '{value}'. Expected #RRGGBB or #AARRGGBB.");

        var rgb = hex[^6..];
        return FromArgb(
            a: hex.Length == 8 ? ParseComponent(hex[..2]) : (byte)255,
            r: ParseComponent(rgb[..2]),
            g: ParseComponent(rgb[2..4]),
            b: ParseComponent(rgb[4..6]));
    }

    private static byte ParseComponent(ReadOnlySpan<char> hex)
    {
        return byte.Parse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
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
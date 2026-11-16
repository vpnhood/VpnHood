using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Net.Toolkit.Converters;

/// <summary>
/// Converts a <see cref="Version"/> to and from its text form, such as <c>1.2.3.4</c>.
/// </summary>
public class VersionConverter : JsonConverter<Version>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentException">The text has fewer than two or more than four parts.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A part is negative.</exception>
    /// <exception cref="FormatException">A part is not a number.</exception>
    /// <exception cref="OverflowException">A part is too large.</exception>
    /// <exception cref="InvalidOperationException">The value is not a string.</exception>
    public override Version Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return Version.Parse(reader.GetString() ?? "");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, Version value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Net.Toolkit.Converters;

/// <summary>
/// Converts a <see cref="TimeSpan"/> to its text form, such as <c>00:05:00</c>, and reads that form or a number of
/// seconds.
/// </summary>
public class TimeSpanConverter : JsonConverter<TimeSpan>
{
    /// <inheritdoc/>
    /// <exception cref="FormatException">The text is not a time interval.</exception>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    /// <exception cref="OverflowException">The interval is too long.</exception>
    /// <exception cref="InvalidOperationException">The value is neither a number nor a string.</exception>
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.Number
            ? TimeSpan.FromSeconds(reader.GetDouble())
            : TimeSpan.Parse(reader.GetString()!);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
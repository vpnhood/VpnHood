using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Net.Toolkit.Net;

/// <summary>
/// Converts a range to and from its text form, such as <c>10.0.0.1-10.0.0.9</c>.
/// </summary>
public class IpRangeConverter : JsonConverter<IpRange>
{
    /// <inheritdoc/>
    /// <exception cref="FormatException">The value is not a range.</exception>
    /// <exception cref="InvalidOperationException">
    /// The reader's token is neither a string, a property name, nor a null; or the range's ends are of different
    /// families or in the wrong order.
    /// </exception>
    public override IpRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return IpRange.Parse(reader.GetString() ?? "");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, IpRange value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Net.Toolkit.Net;

/// <summary>
/// Converts a port range to and from its text form, such as <c>1000-2000</c>.
/// </summary>
public class PortRangeConverter : JsonConverter<PortRange>
{
    /// <inheritdoc/>
    /// <exception cref="FormatException">The value is not a port range.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A bound is out of range.</exception>
    /// <exception cref="InvalidOperationException">The reader's token is neither a string, a property name, nor a null.</exception>
    public override PortRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return PortRange.Parse(reader.GetString() ?? "");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, PortRange value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Net.Toolkit.Net;

/// <summary>
/// Converts a network to and from its text form, such as <c>10.0.0.0/8</c>.
/// </summary>
public class IpNetworkConverter : JsonConverter<IpNetwork>
{
    /// <inheritdoc/>
    /// <exception cref="FormatException">The value is not a network.</exception>
    /// <exception cref="InvalidOperationException">The reader's token is neither a string, a property name, nor a null.</exception>
    public override IpNetwork Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return IpNetwork.Parse(reader.GetString() ?? "");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, IpNetwork value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
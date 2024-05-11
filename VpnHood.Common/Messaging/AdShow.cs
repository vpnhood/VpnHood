using System.Text.Json.Serialization;

namespace VpnHood.Common.Messaging;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AdShow
{
    None,
    Flexible,
    Required
}

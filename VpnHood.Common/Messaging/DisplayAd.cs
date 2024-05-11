using System.Text.Json.Serialization;

namespace VpnHood.Common.Messaging;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DisplayAd
{
    None,
    Flexible,
    Required
}

public enum AdShow
{
    None,
    Flexible,
    Required
}

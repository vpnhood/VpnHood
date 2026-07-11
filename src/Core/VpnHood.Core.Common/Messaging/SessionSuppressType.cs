using System.Text.Json.Serialization;

namespace VpnHood.Core.Common.Messaging;

[JsonConverter(typeof(JsonStringEnumConverter<SessionSuppressType>))]
public enum SessionSuppressType
{
    None,
    YourSelf,
    Other
}
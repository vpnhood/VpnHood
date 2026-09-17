using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Sessions;

[JsonConverter(typeof(JsonStringEnumConverter<SessionSuppressType>))]
public enum SessionSuppressType
{
    None,
    YourSelf,
    Other
}

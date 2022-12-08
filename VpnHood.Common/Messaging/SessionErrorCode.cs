using System.Text.Json.Serialization;

namespace VpnHood.Common.Messaging;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SessionErrorCode
{
    Ok,
    GeneralError,
    SessionClosed,
    SessionSuppressedBy,
    SessionError,
    AccessExpired,
    AccessTrafficOverflow,
    AccessLocked,
    AccessError,
    Maintenance,
    RedirectHost,
    UnsupportedClient,
    UnsupportedServer,
}
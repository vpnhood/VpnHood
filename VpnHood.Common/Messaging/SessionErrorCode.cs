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
    SessionExpired, // not used yet
    AccessExpired,
    AccessCodeRejected,
    AccessLocked,
    AccessTrafficOverflow,
    AccessError,
    NoServerAvailable,
    AdError,
    Maintenance,
    RedirectHost,
    UnsupportedClient,
    UnsupportedServer
}
using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Sessions;

// Why the server refused or ended a session. Names are the server's, on the wire; the mapper is
// exhaustive so a code added to the engine breaks the build rather than arriving as something else.
[JsonConverter(typeof(JsonStringEnumConverter<SessionErrorCode>))]
public enum SessionErrorCode
{
    Ok,
    AccessError,
    PlanRejected,
    GeneralError,
    SessionClosed,
    SessionSuppressedBy,
    SessionError,
    SessionExpired,
    AccessExpired,
    AccessCodeRejected,
    AccessLocked,
    AccessTrafficOverflow,
    DailyLimitExceeded,
    NoServerAvailable,
    PremiumLocation,
    AdError,
    RewardedAdRejected,
    Maintenance,
    RedirectHost,
    UnsupportedClient,
    UnsupportedServer
}

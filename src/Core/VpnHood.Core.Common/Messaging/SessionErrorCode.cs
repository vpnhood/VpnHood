using System.Text.Json.Serialization;

namespace VpnHood.Core.Common.Messaging;

/// <summary>
/// Specifies the outcome of a session's request, or why the session ended.
/// </summary>
/// <remarks>In JSON it is a name.</remarks>
[JsonConverter(typeof(JsonStringEnumConverter<SessionErrorCode>))]
public enum SessionErrorCode
{
    /// <summary>The request succeeded.</summary>
    Ok,

    /// <summary>The access was refused, as for a key or session the server does not know.</summary>
    AccessError,

    /// <summary>The plan the client asked for was refused.</summary>
    PlanRejected,

    /// <summary>Another error.</summary>
    GeneralError,

    /// <summary>The session was closed.</summary>
    SessionClosed,

    /// <summary>
    /// A newer session took the session's place, the same client's or another's over the access's limit of clients
    /// (<see cref="SessionSuppressType"/>).
    /// </summary>
    SessionSuppressedBy,

    /// <summary>The session failed.</summary>
    SessionError,

    /// <summary>The session's time ran out.</summary>
    SessionExpired,

    /// <summary>The access has expired.</summary>
    AccessExpired,

    /// <summary>The premium code was refused.</summary>
    AccessCodeRejected,

    /// <summary>The access is locked.</summary>
    AccessLocked,

    /// <summary>The access has used all its traffic.</summary>
    AccessTrafficOverflow,

    /// <summary>The access's daily limit is used up, as a premium trial's.</summary>
    DailyLimitExceeded,

    /// <summary>No server can take the session, as at the location asked for.</summary>
    NoServerAvailable,

    /// <summary>No free server can take the session at the location asked for, though premium ones are there.</summary>
    PremiumLocation,

    /// <summary>The ad the session required failed.</summary>
    AdError,

    /// <summary>The rewarded ad was not accepted.</summary>
    RewardedAdRejected,

    /// <summary>The server is under maintenance.</summary>
    Maintenance,

    /// <summary>The session is to be made on another server, which the response names.</summary>
    RedirectHost,

    /// <summary>The server does not support this client's version.</summary>
    UnsupportedClient,

    /// <summary>The client does not support this server's version; nothing raises it yet.</summary>
    UnsupportedServer
}
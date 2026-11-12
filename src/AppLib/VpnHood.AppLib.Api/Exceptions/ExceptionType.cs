using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Exceptions;

// The wire values are class names, because that is what an ApiError carries: ExceptionExtensions
// fills TypeName from exceptionType.Name. Every one is written as a literal, including the few
// whose class this project can see, so the table reads as one thing and a reader never has to ask
// why a line differs. Most of them could not be anything else: the engine's exceptions live in
// VpnHood.Core and the ad ones in VpnHood.AppLib.Abstractions, neither of which the contract may
// reference.
//
// The cost of that symmetry, stated once: renaming any of these classes changes what an ApiError
// carries and does NOT change the value here, so the two quietly stop matching and a UI falls back
// to a generic error. Renaming an exception on this list means editing this list in the same
// commit. Changing a value here is a breaking change for every UI that reads it.

/// <summary>
/// Specifies an error the app's API names: each stands for the exception class whose name an API error carries.
/// </summary>
/// <remarks>In JSON it is a name; a name this version does not know fails to read.</remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ExceptionType>))]
public enum ExceptionType
{
    /// <summary>The diagnose found no issue.</summary>
    [EnumMember(Value = "NoErrorFoundException")]
    NoErrorFound,

    /// <summary>The server is under maintenance.</summary>
    [EnumMember(Value = "MaintenanceException")]
    Maintenance,

    /// <summary>The server ended or refused the session; the error's data carries the session's error code.</summary>
    [EnumMember(Value = "SessionException")]
    Session,

    /// <summary>An ad failed.</summary>
    [EnumMember(Value = "AdException")]
    Ad,

    /// <summary>An ad could not be shown.</summary>
    [EnumMember(Value = "ShowAdException")]
    ShowAd,

    /// <summary>An ad needed the app's window in front and found none.</summary>
    [EnumMember(Value = "ShowAdNoUiException")]
    ShowAdNoUi,

    /// <summary>No ad could be loaded.</summary>
    [EnumMember(Value = "LoadAdException")]
    LoadAd,

    /// <summary>The device has no internet, or too slow a connection.</summary>
    [EnumMember(Value = "NoInternetException")]
    NoInternet,

    /// <summary>The VPN connected, but its connection was not stable.</summary>
    [EnumMember(Value = "NoStableVpnException")]
    NoStableVpn,

    /// <summary>The server could not be reached.</summary>
    [EnumMember(Value = "UnreachableServerException")]
    UnreachableServer,

    /// <summary>No proxy could reach the server.</summary>
    [EnumMember(Value = "UnreachableProxyServerException")]
    UnreachableProxyServer,

    /// <summary>No server of the chosen location could be reached.</summary>
    [EnumMember(Value = "UnreachableServerLocationException")]
    UnreachableServerLocation,

    /// <summary>A rewarded ad's reward was not confirmed in time.</summary>
    [EnumMember(Value = "RewardNotEarnedException")]
    RewardNotEarned,

    /// <summary>The VPN service is not ready.</summary>
    [EnumMember(Value = "VpnServiceNotReadyException")]
    VpnServiceNotReady,

    /// <summary>The VPN service could not be reached.</summary>
    [EnumMember(Value = "VpnServiceUnreachableException")]
    VpnServiceUnreachable,

    /// <summary>The VPN service did not answer in time.</summary>
    [EnumMember(Value = "VpnServiceTimeoutException")]
    VpnServiceTimeout,

    /// <summary>The person canceled, as by refusing the VPN's permission.</summary>
    [EnumMember(Value = "UserCanceledException")]
    UserCanceled,

    /// <summary>The connection to the server timed out.</summary>
    [EnumMember(Value = "ConnectionTimeoutException")]
    ConnectionTimeout,

    /// <summary>The server's addresses could not be found.</summary>
    [EnumMember(Value = "EndPointDiscoveryException")]
    EndPointDiscovery,

    /// <summary>The feature is for premium only.</summary>
    [EnumMember(Value = "PremiumOnlyException")]
    PremiumOnly,

    /// <summary>The device's settings point to an ad blocker, as a private DNS the app does not allow.</summary>
    [EnumMember(Value = "AdBlockerException")]
    AdBlocker,

    /// <summary>The system refused the Quick Launch at once.</summary>
    [EnumMember(Value = "RequestQuickLaunchException")]
    RequestQuickLaunch,

    /// <summary>The system took the VPN away from the app, as for another app's VPN.</summary>
    [EnumMember(Value = "VpnServiceRevokedException")]
    VpnServiceRevoked,

    // a store failure in store-agnostic words; the BillingErrorCode rides in Data

    /// <summary>A store failed; the error's data carries its billing error code.</summary>
    [EnumMember(Value = "BillingException")]
    Billing
}

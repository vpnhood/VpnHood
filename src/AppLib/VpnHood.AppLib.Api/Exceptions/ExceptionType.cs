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
[JsonConverter(typeof(JsonStringEnumConverter<ExceptionType>))]
public enum ExceptionType
{
    [EnumMember(Value = "NoErrorFoundException")]
    NoErrorFound,

    [EnumMember(Value = "MaintenanceException")]
    Maintenance,

    [EnumMember(Value = "SessionException")]
    Session,

    [EnumMember(Value = "AdException")]
    Ad,

    [EnumMember(Value = "ShowAdException")]
    ShowAd,

    [EnumMember(Value = "ShowAdNoUiException")]
    ShowAdNoUi,

    [EnumMember(Value = "LoadAdException")]
    LoadAd,

    [EnumMember(Value = "NoInternetException")]
    NoInternet,

    [EnumMember(Value = "NoStableVpnException")]
    NoStableVpn,

    [EnumMember(Value = "UnreachableServerException")]
    UnreachableServer,

    [EnumMember(Value = "UnreachableProxyServerException")]
    UnreachableProxyServer,

    [EnumMember(Value = "UnreachableServerLocationException")]
    UnreachableServerLocation,

    [EnumMember(Value = "RewardNotEarnedException")]
    RewardNotEarned,

    [EnumMember(Value = "VpnServiceNotReadyException")]
    VpnServiceNotReady,

    [EnumMember(Value = "VpnServiceUnreachableException")]
    VpnServiceUnreachable,

    [EnumMember(Value = "VpnServiceTimeoutException")]
    VpnServiceTimeout,

    [EnumMember(Value = "UserCanceledException")]
    UserCanceled,

    [EnumMember(Value = "ConnectionTimeoutException")]
    ConnectionTimeout,

    [EnumMember(Value = "EndPointDiscoveryException")]
    EndPointDiscovery,

    [EnumMember(Value = "PremiumOnlyException")]
    PremiumOnly,

    [EnumMember(Value = "AdBlockerException")]
    AdBlocker,

    [EnumMember(Value = "RequestQuickLaunchException")]
    RequestQuickLaunch,

    [EnumMember(Value = "VpnServiceRevokedException")]
    VpnServiceRevoked,

    // a store failure in store-agnostic words; the BillingErrorCode rides in Data
    [EnumMember(Value = "BillingException")]
    Billing
}

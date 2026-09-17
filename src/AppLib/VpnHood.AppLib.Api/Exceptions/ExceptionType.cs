using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Exceptions;

// The values are the wire's, not a C# reference. Every one is a literal, and none of the classes
// they name is visible from here - they live in the app, in the provider surface, and in the engine,
// none of which the contract may see. Written this way for the reason the arrangement demands:
// renaming a class must NOT silently change what goes over the wire. Changing a value here is a
// breaking change for every UI that reads it.
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

    [EnumMember(Value = "VpnServiceNotReadyException")]
    VpnService,

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
    VpnServiceRevoked
}

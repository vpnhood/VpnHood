using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using VpnHood.AppLib.Api.Exceptions.AdExceptions;

namespace VpnHood.AppLib.Api.Exceptions;

// The wire values are class names, because that is what an ApiError carries: ExceptionExtensions
// fills TypeName from exceptionType.Name, so renaming a class changes the wire whatever is written
// here. nameof is therefore used wherever the class is visible - the rename then travels through
// this enum and into the generated client in one step, instead of leaving a literal that quietly
// stops matching. The engine's exceptions stay literals for the one reason that applies: the
// contract does not reference VpnHood.Core, so their names cannot be spelled here any other way,
// and renaming one of those is a breaking change this file cannot catch.
[JsonConverter(typeof(JsonStringEnumConverter<ExceptionType>))]
public enum ExceptionType
{
    [EnumMember(Value = nameof(NoErrorFoundException))]
    NoErrorFound,

    [EnumMember(Value = "MaintenanceException")]
    Maintenance,

    [EnumMember(Value = "SessionException")]
    Session,

    [EnumMember(Value = nameof(AdException))]
    Ad,

    [EnumMember(Value = nameof(ShowAdException))]
    ShowAd,

    [EnumMember(Value = nameof(ShowAdNoUiException))]
    ShowAdNoUi,

    [EnumMember(Value = nameof(LoadAdException))]
    LoadAd,

    [EnumMember(Value = nameof(NoInternetException))]
    NoInternet,

    [EnumMember(Value = nameof(NoStableVpnException))]
    NoStableVpn,

    [EnumMember(Value = "UnreachableServerException")]
    UnreachableServer,

    [EnumMember(Value = "UnreachableProxyServerException")]
    UnreachableProxyServer,

    [EnumMember(Value = "UnreachableServerLocationException")]
    UnreachableServerLocation,

    [EnumMember(Value = nameof(RewardNotEarnedException))]
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

    [EnumMember(Value = nameof(PremiumOnlyException))]
    PremiumOnly,

    [EnumMember(Value = nameof(AdBlockerException))]
    AdBlocker,

    [EnumMember(Value = nameof(RequestQuickLaunchException))]
    RequestQuickLaunch,

    [EnumMember(Value = "VpnServiceRevokedException")]
    VpnServiceRevoked
}

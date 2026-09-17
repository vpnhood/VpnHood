using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Common.Exceptions;

namespace VpnHood.AppLib.Api.Exceptions;

// The values are the wire's, not a C# reference: ten of these name exception classes the contract
// deliberately cannot see - five in the app (VpnHood.AppLib.Exceptions) and five in the provider
// surface (VpnHood.AppLib.Abstractions.AdExceptions). They are written as literals for that reason -
// renaming the class must NOT silently change what goes over the wire.
[JsonConverter(typeof(JsonStringEnumConverter<ExceptionType>))]
public enum ExceptionType
{
    [EnumMember(Value = "NoErrorFoundException")]
    NoErrorFound,

    [EnumMember(Value = nameof(MaintenanceException))]
    Maintenance,

    [EnumMember(Value = nameof(SessionException))]
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

    [EnumMember(Value = nameof(UnreachableServerException))]
    UnreachableServer,

    [EnumMember(Value = nameof(UnreachableProxyServerException))]
    UnreachableProxyServer,

    [EnumMember(Value = nameof(UnreachableServerLocationException))]
    UnreachableServerLocation,

    [EnumMember(Value = "RewardNotEarnedException")]
    RewardNotEarned,

    [EnumMember(Value = nameof(VpnServiceNotReadyException))]
    VpnServiceNotReady,

    [EnumMember(Value = nameof(VpnServiceUnreachableException))]
    VpnServiceUnreachable,

    [EnumMember(Value = nameof(VpnServiceTimeoutException))]
    VpnServiceTimeout,

    [EnumMember(Value = nameof(VpnServiceNotReadyException))]
    VpnService,

    [EnumMember(Value = nameof(UserCanceledException))]
    UserCanceled,

    [EnumMember(Value = nameof(ConnectionTimeoutException))]
    ConnectionTimeout,

    [EnumMember(Value = nameof(EndPointDiscoveryException))]
    EndPointDiscovery,

    [EnumMember(Value = "PremiumOnlyException")]
    PremiumOnly,

    [EnumMember(Value = "AdBlockerException")]
    AdBlocker,

    [EnumMember(Value = nameof(RequestQuickLaunchException))]
    RequestQuickLaunch,

    [EnumMember(Value = nameof(VpnServiceRevokedException))]
    VpnServiceRevoked
}

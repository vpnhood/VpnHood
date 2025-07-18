using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using VpnHood.AppLib.Exceptions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Manager.Exceptions;
using VpnHood.Core.Common.Exceptions;

namespace VpnHood.AppLib.WebServer.Api;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExceptionType
{
    [EnumMember(Value = nameof(NoErrorFoundException))]
    NoErrorFound,

    [EnumMember(Value = nameof(MaintenanceException))]
    Maintenance,

    [EnumMember(Value = nameof(SessionException))]
    Session,

    [EnumMember(Value = nameof(UiContextNotAvailableException))]
    UiContextNotAvailable,

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
    
    [EnumMember(Value = nameof(UnreachableServerException))]
    UnreachableServer,
    
    [EnumMember(Value = nameof(UnreachableServerLocationException))]
    UnreachableServerLocation,

    [EnumMember(Value = nameof(RewardNotEarnedException))]
    RewardNotEarned,

    [EnumMember(Value = nameof(VpnServiceUnreachableException))]
    VpnServiceUnreachable,

    [EnumMember(Value = nameof(VpnServiceTimeoutException))]
    VpnServiceTimeout,

    [EnumMember(Value = nameof(VpnServiceNotReadyException))]
    VpnServiceNotReady,
    
    [EnumMember(Value = nameof(VpnServiceNotReadyException))]
    VpnService

}
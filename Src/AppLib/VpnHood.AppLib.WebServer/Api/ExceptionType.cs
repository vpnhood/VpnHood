using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace VpnHood.AppLib.WebServer.Api;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExceptionType
{
    [EnumMember(Value = nameof(MaintenanceException))]
    MaintenanceException,

    [EnumMember(Value = nameof(SessionException))]
    SessionException,

    [EnumMember(Value = nameof(UiContextNotAvailableException))]
    UiContextNotAvailableException,

    [EnumMember(Value = nameof(AdException))]
    AdException,    
    
    [EnumMember(Value = nameof(ShowAdException))]
    ShowAdException,
    
    [EnumMember(Value = nameof(ShowAdNoUiException))]
    ShowAdNoUiException,
    
    [EnumMember(Value = nameof(LoadAdException))]
    LoadAdException,
    
    [EnumMember(Value = nameof(NoInternetException))]
    NoInternetException,
    
    [EnumMember(Value = nameof(NoStableVpnException))]
    NoStableVpnException,
    
    [EnumMember(Value = nameof(UnreachableServerException))]
    UnreachableServerException,
    
    [EnumMember(Value = nameof(UnreachableServerLocationException))]
    UnreachableServerLocationException
}
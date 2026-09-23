using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.App;

[JsonConverter(typeof(JsonStringEnumConverter<AppFeature>))]
public enum AppFeature
{
    QuickLaunch,
    AlwaysOn,
    CustomDns,
    SplitIpViaApp,
    SplitIpViaDevice,
    SplitDomain,
    SplitCountry
}

using System.Text.Json.Serialization;

namespace VpnHood.AppLib;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppFeature
{
    QuickLaunch,
    AlwaysOn,
    CustomDns,
    AppIpFilter,
    AdapterIpFilter
}
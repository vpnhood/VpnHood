using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Contracts.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<DnsMode>))]
public enum DnsMode
{
    Default,
    AdapterDns
}

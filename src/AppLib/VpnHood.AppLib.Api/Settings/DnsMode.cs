using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Settings;

/// <summary>Specifies which DNS servers the VPN uses.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DnsMode>))]
public enum DnsMode
{
    /// <summary>The app's own choice.</summary>
    Default,

    /// <summary>The person's own servers (<see cref="UserSettings.DnsServers"/>), where premium allows them; with none set, it falls back to <see cref="Default"/>.</summary>
    AdapterDns
}

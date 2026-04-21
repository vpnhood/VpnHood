using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Dtos;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TcpProxyUsageReason
{
    /// <summary>The user can freely toggle TcpProxy on or off.</summary>
    None,

    /// <summary>The client platform does not support TcpProxy (forced off).</summary>
    ClientNotSupported,

    /// <summary>The server does not support TCP packets, so TcpProxy is required (forced on).</summary>
    ServerRequiredOn,

    /// <summary>The server does not support TcpProxy (forced off).</summary>
    ServerRequiredOff,

    /// <summary>Split-by-domain is active and requires TcpProxy for SNI filtering (forced on).</summary>
    SplitByDomainRequiredOn
}

using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.App;

/// <summary>
/// Specifies why the TCP forwarding switch is locked, if it is; it says nothing of what runs: the switch acts on IPv4,
/// and IPv6 may go by TCP forwarding while it is off.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TcpForwardingLockReason>))]
public enum TcpForwardingLockReason
{
    /// <summary>The user can freely toggle TcpForwarding on or off.</summary>
    None,

    /// <summary>The server does not support TCP packets, so TcpForwarding is required (forced on).</summary>
    ServerRequiredOn,

    /// <summary>The server does not support TcpForwarding (forced off).</summary>
    ServerRequiredOff,

    /// <summary>Split-by-domain is active and requires TcpForwarding for SNI filtering (forced on).</summary>
    SplitDomainRequiredOn
}

using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.SplitTunneling;

/// <summary>
/// Specifies what happens to traffic that wants the tunnel and is for a destination the server does not route: outside
/// its ranges, or of an IP version it does not carry.
/// </summary>
/// <remarks>
/// The user's own exclusions and blocks always win. In JSON it is a name; a name this version does not know fails to
/// read.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<SplitUnsupportedIpMode>))]
public enum SplitUnsupportedIpMode
{
    /// <summary>
    /// The traffic goes directly, outside the VPN: everything keeps working, and an observer sees the traffic the server
    /// declined. Traffic the person's own split includes is blocked all the same, as it was to go inside.
    /// </summary>
    Exclude,

    /// <summary>
    /// The traffic is dropped, never sent around the tunnel. The adapter then captures it, whatever the server's adapter
    /// ranges say, as a packet it does not capture cannot be blocked.
    /// </summary>
    Block
}

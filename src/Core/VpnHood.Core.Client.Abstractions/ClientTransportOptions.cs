using System.Text.Json.Serialization;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Client.Abstractions;

/// <summary>
/// The transport knobs the app tunes and the client forwards, held by reference from
/// <c>AppOptions</c> all the way down to the session config so no layer restates them.
/// <para>
/// The timeouts carry their default here because it is a plain literal. The buffer and count knobs
/// stay null instead: their defaults live in <c>TunnelDefaults</c>, which this project cannot see,
/// so null means "the consumer decides" and each one is resolved once, where its component is
/// constructed. Never resolve a null on the way through — only at the point of use.
/// </para>
/// </summary>
public class ClientTransportOptions
{
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromDays(3);
    public TimeSpan TcpConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan UnstableTimeout { get; set; } = TimeSpan.FromSeconds(60); // connect timeout before pause
    public TimeSpan AutoWaitTimeout { get; set; } = TimeSpan.FromSeconds(30); // auto resume after pause
    public TimeSpan ServerQueryTimeout { get; set; } = TimeSpan.FromSeconds(5);

    // Optional per-platform transport buffer sizes. Low-memory clients (e.g. the iOS Network
    // Extension under the ~50 MB jetsam limit) lower these.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TransferBufferSize? StreamProxyBufferSize { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TransferBufferSize? UdpProxyBufferSize { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TransferBufferSize? PacketChannelBufferSize { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TransferBufferSize? TcpKernelBufferSize { get; set; }

    // Optional kernel buffer used only by TCP connections to the VPN server. This lets
    // memory-constrained clients keep direct/split-flow sockets small without throttling the
    // packet channel, where one outer TCP connection carries all tunneled flows.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TransferBufferSize? TcpPacketChannelKernelBufferSize { get; set; }

    // Optional per-platform UDP proxy scaling. Low-memory clients cap the direct-UDP socket fleet
    // and per-proxy packet queue so a post-kill reconnect flow-storm stays bounded.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int? MaxUdpClientCount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int? MaxUdpDnsClientCount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int? UdpProxyQueueCapacity { get; set; }
}

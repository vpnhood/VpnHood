using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server.Access.Messaging;

namespace VpnHood.Core.Server.Access;

/// <summary>
/// Represents a VPN server's state, as it sends it to its access manager at each status
/// (<see cref="Managers.IAccessManager.Server_UpdateStatus"/>).
/// </summary>
public class ServerStatus
{
    /// <summary>Gets or sets the number of the server's open sessions.</summary>
    public int SessionCount { get; set; }

    /// <summary>
    /// Gets or sets the number of the sessions' stream channels: forwarded TCP connections and packet channels, those
    /// carried over QUIC included.
    /// </summary>
    public int TcpConnectionCount { get; set; }

    /// <summary>Gets or sets the number of the sessions' UDP workers, where the server forwards UDP itself.</summary>
    public int UdpConnectionCount { get; set; }

    /// <summary>Gets or sets the machine's available memory, in bytes, or <see langword="null"/> where not known.</summary>
    public long? AvailableMemory { get; set; }

    /// <summary>
    /// Gets or sets the machine's swap in all, in bytes, or <see langword="null"/> where the server does not manage swap
    /// or could not read it.
    /// </summary>
    public long? TotalSwapMemory { get; set; }

    /// <summary>
    /// Gets or sets the machine's free swap, in bytes, or <see langword="null"/> where the server does not manage swap
    /// or could not read it.
    /// </summary>
    public long? AvailableSwapMemory { get; set; }

    /// <summary>
    /// Gets or sets the TCP congestion control the OS runs, or <see langword="null"/> where the server does not manage
    /// its network.
    /// </summary>
    public string? TcpCongestionControl { get; set; }

    /// <summary>Gets or sets the CPU's use, as the platform reports it, or <see langword="null"/> where not known.</summary>
    public int? CpuUsage { get; set; }

    /// <summary>Gets or sets the server process's working set, in bytes.</summary>
    public long UsedMemory { get; set; }

    /// <summary>Gets or sets the number of the server process's threads.</summary>
    public int ThreadCount { get; set; }

    /// <summary>Gets or sets the sessions' tunnel speed in all, sent and received, in bytes per second.</summary>
    public Traffic TunnelSpeed { get; set; } = new();

    /// <summary>Gets or sets the code of the configuration the server last applied, or <see langword="null"/>.</summary>
    public string? ConfigCode { get; set; }

    /// <summary>
    /// Gets or sets the last configuration's error, as the JSON of an <c>ApiError</c>, or <see langword="null"/>.
    /// </summary>
    public string? ConfigError { get; set; }

    /// <summary>
    /// Gets or sets how the listeners fared at the last configuration, sent once after it; <see langword="null"/>
    /// otherwise.
    /// </summary>
    public ServerHostEndPointStatus[]? EndPointStatuses { get; set; }

    /// <summary>
    /// Gets or sets the sessions' usage since their last report, which goes with each status. The default is none.
    /// </summary>
    public SessionUsage[] SessionUsages { get; set; } = [];
}
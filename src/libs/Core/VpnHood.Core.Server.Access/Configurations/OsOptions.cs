using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Server.Access.Configurations;

/// <summary>
/// Represents what the server sets on its machine's operating system, where it manages it (Linux).
/// </summary>
/// <remarks>
/// A setting left <see langword="null"/> takes its default, which the property of its name ending in <c>Value</c>
/// gives. In JSON, the server's configuration names this group <c>Os</c>. The server changes the running OS only, so a
/// restart of the machine puts back the OS's own values, and it makes no change without admin rights.
/// </remarks>
public class OsOptions
{
    private string? _tcpCongestionControl = "bbr";
    private bool _isTcpCongestionControlSet;

    /// <summary>
    /// Gets or sets the TCP congestion control the server sets on its OS, or <see langword="null"/> or <c>*</c> to put
    /// back the OS's own where the server changed it. The default is <c>bbr</c>.
    /// </summary>
    public string? TcpCongestionControl {
        get => _tcpCongestionControl;
        set {
            _tcpCongestionControl = value;
            _isTcpCongestionControlSet = true;
        }
    }

    /// <summary>
    /// Gets or sets the most connections the OS tracks at once, as the server's NAT needs: a full table drops new
    /// connections, its clients' and its own. <see langword="null"/> for 1,048,576, and 0 to put back the OS's own where
    /// the server changed it; it never goes below the OS's own.
    /// </summary>
    public int? ConnectionTrackingMax { get; set; }

    /// <summary>
    /// Gets or sets how long the OS keeps tracking an established TCP connection that sends nothing, or
    /// <see langword="null"/> for 1 hour, and zero to put back the OS's own where the server changed it: Linux's 5 days
    /// fill the table, as many of a VPN's connections end without a close, such as when a phone changes networks.
    /// </summary>
    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan? ConnectionTrackingTcpIdleTimeout { get; set; }

    /// <summary>
    /// Gets or sets the swap the server wants in all, in megabytes: its own swap file makes up what the system's other
    /// swap lacks, and <see langword="null"/> or 0 removes its own.
    /// </summary>
    public int? SwapMemorySizeMb { get; set; }

    /// <summary>
    /// Gets or sets the network interface the server adds its TCP endpoints' addresses to where the system lacks them;
    /// <see langword="null"/>, empty or <c>*</c> for the first interface.
    /// </summary>
    public string? AddListenerIpsToNetwork { get; set; }

    /// <summary>Gets <see cref="TcpCongestionControl"/>, which has no default of its own here.</summary>
    [JsonIgnore]
    public string? TcpCongestionControlValue => TcpCongestionControl;

    /// <summary>Gets <see cref="ConnectionTrackingMax"/>, or its default, 1,048,576.</summary>
    [JsonIgnore]
    public int ConnectionTrackingMaxValue => ConnectionTrackingMax ?? 1_048_576;

    /// <summary>Gets <see cref="ConnectionTrackingTcpIdleTimeout"/>, or its default, 1 hour.</summary>
    [JsonIgnore]
    public TimeSpan ConnectionTrackingTcpIdleTimeoutValue =>
        ConnectionTrackingTcpIdleTimeout ?? TimeSpan.FromHours(1);

    /// <summary>
    /// Copies over these options each one another set sets; its default <see cref="TcpCongestionControl"/> is no setting.
    /// </summary>
    /// <param name="obj">The options that win where they are set.</param>
    public void Merge(OsOptions obj)
    {
        // a set one only: the other options' default would hide this one's value
        if (obj._isTcpCongestionControlSet && obj.TcpCongestionControl != null)
            TcpCongestionControl = obj.TcpCongestionControl;
        if (obj.ConnectionTrackingMax != null) ConnectionTrackingMax = obj.ConnectionTrackingMax;
        if (obj.ConnectionTrackingTcpIdleTimeout != null)
            ConnectionTrackingTcpIdleTimeout = obj.ConnectionTrackingTcpIdleTimeout;
        if (obj.SwapMemorySizeMb != null) SwapMemorySizeMb = obj.SwapMemorySizeMb;
        if (obj.AddListenerIpsToNetwork != null) AddListenerIpsToNetwork = obj.AddListenerIpsToNetwork;
    }

    /// <summary>
    /// Sets each option left <see langword="null"/> to its default; the swap and interface settings stay as they are.
    /// </summary>
    public void ApplyDefaults()
    {
        TcpCongestionControl = TcpCongestionControlValue;
        ConnectionTrackingMax = ConnectionTrackingMaxValue;
        ConnectionTrackingTcpIdleTimeout = ConnectionTrackingTcpIdleTimeoutValue;
    }
}

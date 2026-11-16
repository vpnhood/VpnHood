using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Server.Access.Configurations;

/// <summary>
/// Represents a VPN server's configuration, as its access manager gives it
/// (<see cref="Managers.IAccessManager.Server_Configure"/>) and a local file may override.
/// </summary>
/// <remarks>
/// A setting left <see langword="null"/> takes its default, which the property of its name ending in <c>Value</c>
/// gives. In JSON, the three groups are named <c>Tracking</c>, <c>Session</c> and <c>NetFilter</c>.
/// </remarks>
public class ServerConfig
{
    /// <summary>Gets or sets what the server's track log records.</summary>
    [JsonPropertyName("Tracking")]
    public TrackingOptions TrackingOptions { get; set; } = new();

    /// <summary>Gets or sets the limits, timeouts and buffers of the server's sessions.</summary>
    [JsonPropertyName("Session")]
    public SessionOptions SessionOptions { get; set; } = new();

    /// <summary>Gets or sets which destinations the server's clients may reach.</summary>
    [JsonPropertyName("NetFilter")]
    public NetFilterOptions NetFilterOptions { get; set; } = new();

    /// <summary>
    /// Gets or sets the TCP endpoints the server listens on, or <see langword="null"/> for port 443 on every IPv4 and
    /// IPv6 address.
    /// </summary>
    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[]? TcpEndPoints { get; set; }

    /// <summary>
    /// Gets or sets the endpoints of the tunnel's UDP channel, or <see langword="null"/> for none.
    /// </summary>
    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[]? UdpEndPoints { get; set; }

    /// <summary>
    /// Gets or sets the endpoints the server takes QUIC on, or <see langword="null"/> for none.
    /// </summary>
    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[]? QuicEndPoints { get; set; }

    /// <summary>
    /// Gets or sets the reverse proxies whose published addresses the server trusts to name the client's IP in
    /// X-Forwarded-For, which the access manager locks and locates; or <see langword="null"/> for Cloudflare's alone.
    /// </summary>
    /// <remarks>Any other sender is taken at its own address.</remarks>
    public ReverseProxyProvider[]? ReverseProxyProviders { get; set; }

    /// <summary>
    /// Gets or sets more addresses the server trusts to name the client's IP in X-Forwarded-For, or
    /// <see langword="null"/> for none.
    /// </summary>
    public IpRange[]? ReverseProxyIpRanges { get; set; }

    /// <summary>
    /// Gets or sets the DNS servers the server gives its clients, which its filter lets out of its ranges, or
    /// <see langword="null"/> for Google's.
    /// </summary>
    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public IPAddress[]? DnsServers { get; set; }

    /// <summary>
    /// Gets or sets how often the server sends its status to the access manager, or <see langword="null"/> for 2
    /// minutes; the sessions' sync interval (<see cref="SessionOptions.SyncInterval"/>) caps it.
    /// </summary>
    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan? UpdateStatusInterval { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the server's logging redacts the addresses and IDs it formats, which the
    /// tracking options' records and some reports leave as they are, or <see langword="null"/> for
    /// <see langword="true"/>.
    /// </summary>
    public bool? LogAnonymizer { get; set; }

    /// <summary>
    /// Gets or sets the server's secret, which the server token carries and the requests' proofs come from, or
    /// <see langword="null"/> to keep the server's own.
    /// </summary>
    public byte[]? ServerSecret { get; set; }

    /// <summary>
    /// Gets or sets the configuration's code, which the server reports in its status: an answer with another code makes
    /// it ask for its configuration again. The default is empty.
    /// </summary>
    public string ConfigCode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the thread pool's least I/O completion threads: <see langword="null"/> for 30 times the pool's
    /// minimum at the time, at each configuration, and 0 to leave it.
    /// </summary>
    public int? MinCompletionPortThreads { get; set; }

    /// <summary>
    /// Gets or sets the thread pool's most I/O completion threads: <see langword="null"/> for 65,535, chosen so I/O
    /// rather slows together than waits in a queue, and 0 to leave the runtime's.
    /// </summary>
    public int? MaxCompletionPortThreads { get; set; }

    /// <summary>
    /// Gets or sets the swap the server wants in all, in megabytes, where it manages swap (Linux): its own swap file
    /// makes up what the system's other swap lacks, and <see langword="null"/> or 0 removes its own.
    /// </summary>
    public int? SwapMemorySizeMb { get; set; }

    /// <summary>
    /// Gets or sets the network interface the server adds its TCP endpoints' addresses to where the system lacks them,
    /// where it manages its network (Linux); <see langword="null"/>, empty or <c>*</c> for the first interface.
    /// </summary>
    public string? AddListenerIpsToNetwork { get; set; }

    /// <summary>
    /// Gets or sets the TLS certificates the server presents; at least one is required. The default is none.
    /// </summary>
    public CertificateData[] Certificates { get; set; } = [];

    /// <summary>
    /// Gets or sets the TCP congestion control the server sets on its OS where it manages its network (Linux), or
    /// <see langword="null"/> to leave the OS's. The default is <c>bbr</c>.
    /// </summary>
    public string? TcpCongestionControl { get; set; } = "bbr";

    /// <summary>
    /// Gets or sets a value indicating whether the server answers ACME HTTP-01 challenges on port 80 of its TCP
    /// endpoints' addresses, with the access manager's key authorizations, or <see langword="null"/> for
    /// <see langword="false"/>.
    /// </summary>
    public bool? EnableHttp01Challenge { get; set; }

    /// <summary>Gets <see cref="TcpEndPoints"/>, or its default: port 443 on every IPv4 and IPv6 address.</summary>
    [JsonIgnore]
    public IPEndPoint[] TcpEndPointsValue =>
        TcpEndPoints ?? [new IPEndPoint(IPAddress.Any, 443), new IPEndPoint(IPAddress.IPv6Any, 443)];

    /// <summary>Gets <see cref="QuicEndPoints"/>, or its default, none.</summary>
    [JsonIgnore]
    public IPEndPoint[] QuicEndPointsValue =>
        QuicEndPoints ?? [];

    /// <summary>Gets <see cref="UdpEndPoints"/>, or its default, none.</summary>
    [JsonIgnore]
    public IPEndPoint[] UdpEndPointsValue =>
        UdpEndPoints ?? [];

    /// <summary>
    /// Gets <see cref="ReverseProxyProviders"/>, or its default, Cloudflare alone: not CloudFront, as edge code on any
    /// AWS account may write X-Forwarded-For, and CloudFront marks none of it.
    /// </summary>
    [JsonIgnore]
    public ReverseProxyProvider[] ReverseProxyProvidersValue =>
        ReverseProxyProviders ?? [ReverseProxyProvider.Cloudflare];

    /// <summary>Gets <see cref="ReverseProxyIpRanges"/>, or its default, none.</summary>
    [JsonIgnore]
    public IpRange[] ReverseProxyIpRangesValue => ReverseProxyIpRanges ?? [];

    /// <summary>Gets <see cref="DnsServers"/>, or its default, Google's.</summary>
    [JsonIgnore]
    public IPAddress[] DnsServersValue => DnsServers ?? [.. IPAddressUtil.GoogleDnsServers];

    /// <summary>Gets <see cref="UpdateStatusInterval"/>, or its default, 2 minutes.</summary>
    [JsonIgnore]
    public TimeSpan UpdateStatusIntervalValue => UpdateStatusInterval ?? TimeSpan.FromSeconds(120);

    /// <summary>Gets <see cref="LogAnonymizer"/>, or its default, <see langword="true"/>.</summary>
    [JsonIgnore]
    public bool LogAnonymizerValue => LogAnonymizer ?? true;

    /// <summary>Gets <see cref="TcpCongestionControl"/>, which has no default of its own here.</summary>
    [JsonIgnore]
    public string? TcpCongestionControlValue => TcpCongestionControl;

    /// <summary>Gets <see cref="EnableHttp01Challenge"/>, or its default, <see langword="false"/>.</summary>
    [JsonIgnore]
    public bool EnableHttp01ChallengeValue => EnableHttp01Challenge ?? false;

    /// <summary>
    /// Copies over this configuration each setting another one sets, but its code and its certificates, as a local
    /// file's over the access manager's.
    /// </summary>
    /// <param name="obj">The configuration whose settings win where it sets them.</param>
    public void Merge(ServerConfig obj)
    {
        TrackingOptions.Merge(obj.TrackingOptions);
        SessionOptions.Merge(obj.SessionOptions);
        NetFilterOptions.Merge(obj.NetFilterOptions);
        if (obj.TcpEndPoints != null) TcpEndPoints = obj.TcpEndPoints;
        if (obj.UdpEndPoints != null) UdpEndPoints = obj.UdpEndPoints;
        if (obj.QuicEndPoints != null) QuicEndPoints = obj.QuicEndPoints;
        if (obj.ReverseProxyProviders != null) ReverseProxyProviders = obj.ReverseProxyProviders;
        if (obj.ReverseProxyIpRanges != null) ReverseProxyIpRanges = obj.ReverseProxyIpRanges;
        if (obj.UpdateStatusInterval != null) UpdateStatusInterval = obj.UpdateStatusInterval;
        if (obj.DnsServers != null) DnsServers = obj.DnsServers;
        if (obj.LogAnonymizer != null) LogAnonymizer = obj.LogAnonymizer;
        if (obj.ServerSecret != null) ServerSecret = obj.ServerSecret;
        if (obj.MinCompletionPortThreads != null) MinCompletionPortThreads = obj.MinCompletionPortThreads;
        if (obj.MaxCompletionPortThreads != null) MaxCompletionPortThreads = obj.MaxCompletionPortThreads;
        if (obj.SwapMemorySizeMb != null) SwapMemorySizeMb = obj.SwapMemorySizeMb;
        if (obj.AddListenerIpsToNetwork != null) AddListenerIpsToNetwork = obj.AddListenerIpsToNetwork;
        if (obj.TcpCongestionControl != null) TcpCongestionControl = obj.TcpCongestionControl;
        if (obj.EnableHttp01Challenge != null) EnableHttp01Challenge = obj.EnableHttp01Challenge;
    }

    /// <summary>
    /// Sets each setting left <see langword="null"/> to its default, so the configuration shows the values it runs with;
    /// the QUIC endpoints, the server's secret and the thread, swap and interface settings stay as they are.
    /// </summary>
    public void ApplyDefaults()
    {
        TrackingOptions.ApplyDefaults();
        SessionOptions.ApplyDefaults();
        NetFilterOptions.ApplyDefaults();
        TcpEndPoints = TcpEndPointsValue;
        UdpEndPoints = UdpEndPointsValue;
        ReverseProxyProviders = ReverseProxyProvidersValue;
        ReverseProxyIpRanges = ReverseProxyIpRangesValue;
        DnsServers = DnsServersValue;
        UpdateStatusInterval = UpdateStatusIntervalValue;
        LogAnonymizer = LogAnonymizerValue;
        TcpCongestionControl = TcpCongestionControlValue;
        EnableHttp01Challenge = EnableHttp01ChallengeValue;
    }
}
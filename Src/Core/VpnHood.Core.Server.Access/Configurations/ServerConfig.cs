using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Converters;
using VpnHood.Core.Common.Net;

namespace VpnHood.Core.Server.Access.Configurations;

public class ServerConfig
{
    [JsonPropertyName("Tracking")] public TrackingOptions TrackingOptions { get; set; } = new();

    [JsonPropertyName("Session")] public SessionOptions SessionOptions { get; set; } = new();

    [JsonPropertyName("NetFilter")] public NetFilterOptions NetFilterOptions { get; set; } = new();

    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[]? TcpEndPoints { get; set; }

    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[]? UdpEndPoints { get; set; }

    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public IPAddress[]? DnsServers { get; set; }

    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan? UpdateStatusInterval { get; set; }

    public bool? LogAnonymizer { get; set; }
    public byte[]? ServerSecret { get; set; }
    public string ConfigCode { get; set; } = string.Empty;
    public int? MinCompletionPortThreads { get; set; }
    public int? MaxCompletionPortThreads { get; set; }
    public int? SwapMemorySizeMb { get; set; }
    public string? AddListenerIpsToNetwork { get; set; }
    public DnsChallenge? DnsChallenge { get; set; }
    public CertificateData[] Certificates { get; set; } = [];
    public string? TcpCongestionControl { get; set; } = "bbr";

    // Inherit
    [JsonIgnore]
    public IPEndPoint[] TcpEndPointsValue =>
        TcpEndPoints ?? [new IPEndPoint(IPAddress.Any, 443), new IPEndPoint(IPAddress.IPv6Any, 443)];

    [JsonIgnore]
    public IPEndPoint[] UdpEndPointsValue =>
        UdpEndPoints ?? [new IPEndPoint(IPAddress.Any, 0), new IPEndPoint(IPAddress.IPv6Any, 0)];

    [JsonIgnore] public IPAddress[] DnsServersValue => DnsServers ?? IPAddressUtil.GoogleDnsServers;
    [JsonIgnore] public TimeSpan UpdateStatusIntervalValue => UpdateStatusInterval ?? TimeSpan.FromSeconds(120);
    [JsonIgnore] public bool LogAnonymizerValue => LogAnonymizer ?? true;
    [JsonIgnore] public string? TcpCongestionControlValue => TcpCongestionControl;


    public void Merge(ServerConfig obj)
    {
        TrackingOptions.Merge(obj.TrackingOptions);
        SessionOptions.Merge(obj.SessionOptions);
        NetFilterOptions.Merge(obj.NetFilterOptions);
        if (obj.TcpEndPoints != null) TcpEndPoints = obj.TcpEndPoints;
        if (obj.UdpEndPoints != null) UdpEndPoints = obj.UdpEndPoints;
        if (obj.UpdateStatusInterval != null) UpdateStatusInterval = obj.UpdateStatusInterval;
        if (obj.DnsServers != null) DnsServers = obj.DnsServers;
        if (obj.LogAnonymizer != null) LogAnonymizer = obj.LogAnonymizer;
        if (obj.ServerSecret != null) ServerSecret = obj.ServerSecret;
        if (obj.MinCompletionPortThreads != null) MinCompletionPortThreads = obj.MinCompletionPortThreads;
        if (obj.MaxCompletionPortThreads != null) MaxCompletionPortThreads = obj.MaxCompletionPortThreads;
        if (obj.SwapMemorySizeMb != null) SwapMemorySizeMb = obj.SwapMemorySizeMb;
        if (obj.AddListenerIpsToNetwork != null) AddListenerIpsToNetwork = obj.AddListenerIpsToNetwork;
        if (obj.DnsChallenge != null) DnsChallenge = obj.DnsChallenge;
        if (obj.TcpCongestionControl != null) TcpCongestionControl = obj.TcpCongestionControl;
    }

    public void ApplyDefaults()
    {
        TrackingOptions.ApplyDefaults();
        SessionOptions.ApplyDefaults();
        NetFilterOptions.ApplyDefaults();
        TcpEndPoints = TcpEndPointsValue;
        UdpEndPoints = UdpEndPointsValue;
        DnsServers = DnsServersValue;
        UpdateStatusInterval = UpdateStatusIntervalValue;
        LogAnonymizer = LogAnonymizerValue;
        TcpCongestionControl = TcpCongestionControlValue;
    }
}
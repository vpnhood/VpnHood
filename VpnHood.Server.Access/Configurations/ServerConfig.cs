using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server.Access.Configurations;

public class ServerConfig
{
    [JsonPropertyName("Tracking")]
    public TrackingOptions TrackingOptions { get; set; } = new();

    [JsonPropertyName("Session")]
    public SessionOptions SessionOptions { get; set; } = new();

    [JsonPropertyName("NetFilter")]
    public NetFilterOptions NetFilterOptions { get; set; } = new();

    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[]? TcpEndPoints { get; set; }

    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[]? UdpEndPoints { get; set; }


    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan? UpdateStatusInterval { get; set; }
    public bool? LogAnonymizer { get; set; }
    public byte[]? ServerSecret { get; set; } 
    public string ConfigCode { get; set; } = string.Empty;
    public int? MinCompletionPortThreads { get; set; }
    public int? MaxCompletionPortThreads { get; set; }

    // Inherit
    [JsonIgnore] public IPEndPoint[] TcpEndPointsValue => TcpEndPoints ?? new IPEndPoint[] { new(IPAddress.Any, 443), new(IPAddress.IPv6Any, 443) };
    [JsonIgnore] public IPEndPoint[] UdpEndPointsValue => UdpEndPoints ?? new IPEndPoint[] { new(IPAddress.Any, 0), new(IPAddress.IPv6Any, 0) };
    [JsonIgnore] public TimeSpan UpdateStatusIntervalValue => UpdateStatusInterval ?? TimeSpan.FromSeconds(120);
    [JsonIgnore] public bool LogAnonymizerValue => LogAnonymizer ?? true;
    [JsonIgnore] public byte[]? ServerSecretValue => ServerSecret;

    public void Merge(ServerConfig obj)
    {
        TrackingOptions.Merge(obj.TrackingOptions);
        SessionOptions.Merge(obj.SessionOptions);
        NetFilterOptions.Merge(obj.NetFilterOptions);
        if (obj.TcpEndPoints != null) TcpEndPoints = obj.TcpEndPoints;
        if (obj.UdpEndPoints != null) UdpEndPoints = obj.UdpEndPoints;
        if (obj.UpdateStatusInterval != null) UpdateStatusInterval = obj.UpdateStatusInterval;
        if (obj.LogAnonymizer != null) LogAnonymizer = obj.LogAnonymizer;
        if (obj.ServerSecret != null) ServerSecret = obj.ServerSecret;
        if (obj.MinCompletionPortThreads != null) MinCompletionPortThreads = obj.MinCompletionPortThreads;
        if (obj.MaxCompletionPortThreads != null) MaxCompletionPortThreads = obj.MaxCompletionPortThreads;
    }

    public void ApplyDefaults()
    {
        TrackingOptions.ApplyDefaults();
        SessionOptions.ApplyDefaults();
        NetFilterOptions.ApplyDefaults();
        TcpEndPoints = TcpEndPointsValue;
        UdpEndPoints = UdpEndPointsValue;
        UpdateStatusInterval = UpdateStatusIntervalValue;
        LogAnonymizer = LogAnonymizerValue;
        ServerSecret = ServerSecretValue;
    }
}
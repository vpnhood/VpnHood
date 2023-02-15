using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server.Configurations;

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

    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan? UpdateStatusInterval { get; set; }
    public int? MinCompletionPortThreads { get; set; }
    public int? MaxCompletionPortThreads { get; set; }
    public bool? LogAnonymizer { get; set; }
    public bool? AllowIpV6 { get; set; }
    public string ConfigCode { get; set; } = string.Empty;

    [JsonIgnore] public IPEndPoint[] TcpEndPointsValue => TcpEndPoints ?? new IPEndPoint[] { new(IPAddress.Any, 443), new(IPAddress.IPv6Any, 443) };
    [JsonIgnore] public TimeSpan UpdateStatusIntervalValue => UpdateStatusInterval ?? TimeSpan.FromSeconds(120);
    [JsonIgnore] public bool LogAnonymizerValue => LogAnonymizer ?? true;
    [JsonIgnore] public bool AllowIpV6Value => AllowIpV6 ?? true;

}
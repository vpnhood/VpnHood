using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server;

public class ServerConfig
{
    [JsonPropertyName("Tracking")]
    public TrackingOptions TrackingOptions { get; set; } = new();

    [JsonPropertyName("Session")]
    public SessionOptions SessionOptions { get; set; } = new();
    public string ConfigCode { get; set; } = string.Empty;

    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[] TcpEndPoints { get; set; } = { new(IPAddress.Any, 443), new(IPAddress.IPv6Any, 443) };

    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan UpdateStatusInterval { get; set; } = TimeSpan.FromSeconds(120);
    public int? MinCompletionPortThreads { get; set; }
    public int? MaxCompletionPortThreads { get; set; }
    public bool LogAnonymizer { get; set; } = false;
    public bool AllowIpV6 { get; set; } = true;
}
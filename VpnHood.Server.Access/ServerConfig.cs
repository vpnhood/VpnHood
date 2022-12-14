using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server;

public class ServerConfig
{
    [JsonPropertyName("Tracking")]
    public TrackingOptions TrackingOptions { get; set; } = new();

    //todo legacy for version 324 and older
    [Obsolete]
    [JsonPropertyName("TrackingOptions")]
    public TrackingOptions TrackingOptionsLegacy { get=>TrackingOptions; set=>TrackingOptions = value; } 

    [JsonPropertyName("Session")]
    public SessionOptions SessionOptions { get; set; } = new();

    //todo legacy for version 324 and older
    [Obsolete]
    [JsonPropertyName("SessionOptions")]
    public SessionOptions SessionOptionsLegacy { get => SessionOptions; set => SessionOptions = value; }

    public int UdpPort { get; set; }

    public string ConfigCode { get; set; }

    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[] TcpEndPoints { get; set; }

    [JsonConverter(typeof(TimeSpanConverter))]
    public TimeSpan UpdateStatusInterval { get; set; } = TimeSpan.FromSeconds(120);

    public ServerConfig(IPEndPoint[] tcpEndPoints, string configCode)
    {
        TcpEndPoints = tcpEndPoints;
        ConfigCode = configCode;
    }
}
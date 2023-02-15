using System.Text.Json.Serialization;

namespace VpnHood.Server.Configurations;

public class TrackingOptions
{
    public bool? TrackClientIp { get; set; }
    public bool? TrackLocalPort { get; set; }
    public bool? TrackDestinationIp { get; set; }
    public bool? TrackDestinationPort { get; set; }
    public bool? TrackUdp { get; set; }
    public bool? TrackTcp { get; set; }
    public bool? TrackIcmp { get; set; }

    // Calculate
    [JsonIgnore] public bool IsEnabled => TrackClientIpValue || TrackLocalPortValue || TrackDestinationIpValue || TrackDestinationPortValue;
    [JsonIgnore] public bool TrackClientIpValue => TrackClientIp ?? false;
    [JsonIgnore] public bool TrackLocalPortValue => TrackLocalPort ?? false;
    [JsonIgnore] public bool TrackDestinationIpValue => TrackDestinationIp ?? false;
    [JsonIgnore] public bool TrackDestinationPortValue => TrackDestinationPort ?? false;
    [JsonIgnore] public bool TrackUdpValue => TrackUdp ?? true;
    [JsonIgnore] public bool TrackTcpValue => TrackTcp ?? true;
    [JsonIgnore] public bool TrackIcmpValue => TrackIcmp ?? true;
}
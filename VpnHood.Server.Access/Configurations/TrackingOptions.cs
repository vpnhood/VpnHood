using System;
using System.Linq;
using System.Reflection;
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

    public void Merge(TrackingOptions obj)
    {
        if (obj.TrackClientIp != null) TrackClientIp = obj.TrackClientIp;
        if (obj.TrackLocalPort != null) TrackLocalPort = obj.TrackLocalPort;
        if (obj.TrackDestinationIp != null) TrackDestinationIp = obj.TrackDestinationIp;
        if (obj.TrackDestinationPort != null) TrackDestinationPort = obj.TrackDestinationPort;
        if (obj.TrackUdp != null) TrackUdp = obj.TrackUdp;
        if (obj.TrackTcp != null) TrackTcp = obj.TrackTcp;
        if (obj.TrackIcmp != null) TrackIcmp = obj.TrackIcmp;
    }

    public void ApplyDefaults()
    {
        TrackClientIp = TrackClientIpValue;
        TrackLocalPort = TrackLocalPortValue;
        TrackDestinationIp = TrackDestinationIpValue;
        TrackDestinationPort = TrackDestinationPortValue;
        TrackUdp = TrackUdpValue;
        TrackTcp = TrackTcpValue;
        TrackIcmp = TrackIcmpValue;
    }

}
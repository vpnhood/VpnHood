namespace VpnHood.Core.Filtering.Abstractions;

public class NetFilter
{
    public required IIpMapper? IpMapper { get; init; }
    public required IIpFilter? IpFilter { get; init; }
}
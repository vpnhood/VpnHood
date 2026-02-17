namespace VpnHood.Core.Filtering.Abstractions;

public class NetFilter : IDisposable
{
    public required IIpMapper? IpMapper { get; init; }
    public required IIpFilter? IpFilter { get; init; }
    public required IDomainFilter? DomainFilter { get; init; }
    public void Dispose()
    {
        IpMapper?.Dispose();
        IpFilter?.Dispose();
        DomainFilter?.Dispose();
    }
}
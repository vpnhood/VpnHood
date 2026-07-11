namespace VpnHood.Core.Filtering.Abstractions;

public class NetFilter : IDisposable
{
    public IIpMapper? IpMapper { get; init; }
    public IIpFilter? IpFilter { get; init; }
    public IDomainFilter? DomainFilter { get; init; }
    public void Dispose()
    {
        IpMapper?.Dispose();
        IpFilter?.Dispose();
        DomainFilter?.Dispose();
    }
}
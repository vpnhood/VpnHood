namespace VpnHood.Core.Filtering.Abstractions;

public class NetFilter : IDisposable
{
    public IIpMapper? IpMapper { get; init; }
    public IIpFilter? IpFilter { get; init; }
    public IDomainFilter? DomainFilter { get; init; }

    // rolls the Reconfigure command down both filter pipes; the stages that own external
    // configuration react, everything else forwards (see IIpFilter.Reconfigure)
    public void Reconfigure()
    {
        IpFilter?.Reconfigure();
        DomainFilter?.Reconfigure();
    }

    public void Dispose()
    {
        IpMapper?.Dispose();
        IpFilter?.Dispose();
        DomainFilter?.Dispose();
    }
}
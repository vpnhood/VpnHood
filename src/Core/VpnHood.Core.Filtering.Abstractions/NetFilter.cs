using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Filtering.Abstractions;

public class NetFilter : IDisposable
{
    public IIpMapper? IpMapper { get; init; }
    public IIpFilter? IpFilter { get; init; }
    public IDomainFilter? DomainFilter { get; init; }

    // rolls the Reconfigure command down both filter pipes; the stages that own external
    // configuration react, everything else forwards (see IIpFilter.Reconfigure)
    public async Task Reconfigure(CancellationToken cancellationToken)
    {
        if (IpFilter != null)
            await IpFilter.Reconfigure(cancellationToken).Vhc();

        if (DomainFilter != null)
            await DomainFilter.Reconfigure(cancellationToken).Vhc();
    }

    public void Dispose()
    {
        IpMapper?.Dispose();
        IpFilter?.Dispose();
        DomainFilter?.Dispose();
    }
}
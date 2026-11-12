using VpnHood.Core.Filtering.Sqlite;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Services;

/// <summary>
/// Provides the splits' databases to the VPN service: at a connect and at every reconfigure, it asks each split's
/// service for its database and writes the lists the VPN service reads, in its config folder.
/// </summary>
/// <param name="vpnServiceConfigFolder">The VPN service's config folder, the one it is sure to read.</param>
/// <param name="splitCountryService">The split by country's service.</param>
/// <param name="splitIpViaAppService">The split by IP address's service.</param>
/// <param name="splitDomainService">The split by domain's service.</param>
public class SplitDbPublisherService(
    string vpnServiceConfigFolder,
    SplitCountryService splitCountryService,
    SplitIpViaAppService splitIpViaAppService,
    SplitDomainService splitDomainService)
{
    private readonly AsyncLock _publishLock = new();

    // One at a time: a connect's and a reconfigure's would build a changed db together, over one temp
    // file, and each manifest write sweeps what the other just listed.
    internal async Task Publish(CancellationToken cancellationToken)
    {
        using var scopeLock = await _publishLock.LockAsync(cancellationToken).Vhc();
        var ipDbFolder = Path.Combine(vpnServiceConfigFolder, SplitDbManifest.IpFiltersFolderName);
        string?[] ipDbPaths = [
            await splitCountryService.EnsureSplitIpDb(ipDbFolder, cancellationToken).Vhc(),
            await splitIpViaAppService.EnsureSplitIpDb(ipDbFolder, cancellationToken).Vhc()
        ];
        await SplitDbManifest.Write(ipDbFolder, ipDbPaths.OfType<string>().ToArray(), cancellationToken).Vhc();

        var domainDbFolder = Path.Combine(vpnServiceConfigFolder, SplitDbManifest.DomainFiltersFolderName);
        var domainDbPath = await splitDomainService.EnsureSplitDomainDb(domainDbFolder, cancellationToken).Vhc();
        await SplitDbManifest.Write(domainDbFolder, domainDbPath != null ? [domainDbPath] : [], cancellationToken)
            .Vhc();
    }
}

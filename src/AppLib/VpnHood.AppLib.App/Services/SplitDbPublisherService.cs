using VpnHood.Core.Filtering.Sqlite;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.AppLib.Services;

// Publishes the current split filter db set for the VpnService: asks each split service for its db
// (each service owns its whole activity decision — settings gate, premium plan, build) and writes the
// folder manifests the VpnService's filter chains read, at connect and on every reconfigure. The
// manifest write also sweeps superseded files. An inactive feature is simply absent from the set — off
// is the empty case of the same flow, never inferred from files on disk.
// The manifest MUST be written by this aggregator, never by a single service: the ip folder is SHARED
// (split-country + split-ip-via-app) and a manifest write sweeps unlisted db files, so a one-service
// write would sweep its sibling's live db.
// The dbs live under VpnServiceConfigFolder because that is the only folder the VpnService process is
// guaranteed to read (on iOS it is the shared app-group container).
public class SplitDbPublisherService(
    string vpnServiceConfigFolder,
    SplitCountryService splitCountryService,
    SplitIpViaAppService splitIpViaAppService,
    SplitDomainService splitDomainService)
{
    public async Task Publish(CancellationToken cancellationToken)
    {
        var ipDbFolder = Path.Combine(vpnServiceConfigFolder, SplitDbManifest.IpFiltersFolderName);
        string?[] ipDbPaths = [
            await splitCountryService.EnsureSplitIpDb(ipDbFolder, cancellationToken).Vhc(),
            await splitIpViaAppService.EnsureSplitIpDb(ipDbFolder, cancellationToken).Vhc()
        ];
        SplitDbManifest.Write(ipDbFolder, ipDbPaths.OfType<string>().ToArray());

        var domainDbFolder = Path.Combine(vpnServiceConfigFolder, SplitDbManifest.DomainFiltersFolderName);
        var domainDbPath = await splitDomainService.EnsureSplitDomainDb(domainDbFolder, cancellationToken).Vhc();
        SplitDbManifest.Write(domainDbFolder, domainDbPath != null ? [domainDbPath] : []);
    }
}

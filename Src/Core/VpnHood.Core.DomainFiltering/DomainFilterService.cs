using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.DomainFiltering.Tls;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.DomainFiltering;

public class DomainFilterService(DomainFilter domainFilter, bool forceLogSni, EventId eventId, int bufferSize)
{
    public bool IsEnabled =>
        forceLogSni ||
        domainFilter.Includes.Length > 0 ||
        domainFilter.Excludes.Length > 0 ||
        domainFilter.Blocks.Length > 0;

    public async Task<DomainFilterResult> Process(Stream tlsStream, IPAddress remoteAddress,
        CancellationToken cancellationToken)
    {
        // none if domain filter is empty
        if (!IsEnabled)
            return new DomainFilterResult {
                Action = DomainFilterAction.None,
                DomainName = null,
                ReadData = Memory<byte>.Empty
            };

        // extract SNI
        var sniData = await TlsSniExtractor.ExtractSni(tlsStream, eventId, bufferSize, cancellationToken).Vhc();
        VhLogger.Instance.LogInformation(eventId,
            "Domain: {Domain}, DestEp: {IP}",
            VhLogger.FormatHostName(sniData.Sni), VhLogger.Format(remoteAddress));

        // no SNI
        var res = new DomainFilterResult {
            DomainName = sniData.Sni,
            ReadData = sniData.ReadData,
            Action = Process(sniData.Sni)
        };

        return res;
    }

}
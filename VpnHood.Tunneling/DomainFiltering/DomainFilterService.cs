using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
using VpnHood.Tunneling.Utils;

namespace VpnHood.Tunneling.DomainFiltering;

public class DomainFilterService(DomainFilter domainFilter, bool forceLogSni)
{
    public async Task<DomainFilterResult> Process(Stream tlsStream, IPAddress remoteAddress, CancellationToken cancellationToken)
    {
        // none if domain filter is empty
        if (!forceLogSni && domainFilter.Includes.Length == 0 && domainFilter.Excludes.Length == 0 && domainFilter.Blocks.Length == 0)
            return new DomainFilterResult
            {
                Action = DomainFilterAction.None
            };

        // extract SNI
        var sniData = await SniExtractor.ExtractSni(tlsStream, cancellationToken).VhConfigureAwait();
        VhLogger.Instance.LogInformation(GeneralEventId.Sni,
            "Domain: {Domain}, DestEp: {IP}",
            VhLogger.FormatHostName(sniData.Sni), VhLogger.Format(remoteAddress));

        // no SNI
        var res = new DomainFilterResult
        {
            DomainName = sniData.Sni,
            ReadData = sniData.ReadData,
            Action = Process(sniData.Sni)
        };

        return res;
    }

    private DomainFilterAction Process(string? domain)
    {
        var topDomains = ExtractTopDomains(domain);
        foreach (var topDomain in topDomains)
        {
            var res = ProcessInternal(topDomain, domainFilter);
            if (res != DomainFilterAction.None)
                return res;
        }

        return domainFilter.Includes.Length == 0
            ? DomainFilterAction.None
            : DomainFilterAction.Exclude;
    }

    public static DomainFilterAction ProcessInternal(string domain, DomainFilter domainFilter)
    {
        var topDomains = ExtractTopDomains(domain);
        foreach (var topDomain in topDomains)
        {
            if (IsMatch(topDomain, domainFilter.Blocks))
                return DomainFilterAction.Block;

            if (IsMatch(topDomain, domainFilter.Excludes))
                return DomainFilterAction.Exclude;

            if (IsMatch(topDomain, domainFilter.Includes))
                return DomainFilterAction.Include;
        }

        return DomainFilterAction.None;
    }

    private static bool IsMatch(string domain, string[] domains)
    {
        return domains.Contains(domain);
    }

    private static string[] ExtractTopDomains(string? domain)
    {
        if (string.IsNullOrEmpty(domain))
            return [];

        var topDomains = new List<string>();
        var parts = domain.Split('.');
        for (var i = 0; i < parts.Length; i++)
        {
            var topDomain = string.Join('.', parts.Skip(i));
            topDomains.Add(topDomain);
        }

        return topDomains.ToArray();
    }
}
using System.Text.Json;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Server.ReverseProxies;

// where each provider publishes the addresses its proxies connect from, each in a format of its own
internal static class ReverseProxyFeed
{
    public static Uri GetUrl(ReverseProxyProvider provider)
    {
        return provider switch {
            ReverseProxyProvider.Cloudflare => new Uri("https://api.cloudflare.com/client/v4/ips"),
            ReverseProxyProvider.CloudFront => new Uri("https://ip-ranges.amazonaws.com/ip-ranges.json"),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown reverse proxy provider.")
        };
    }

    public static IReadOnlyList<IpRange> Parse(ReverseProxyProvider provider, string json)
    {
        using var document = JsonDocument.Parse(json);
        var ipRanges = provider switch {
            ReverseProxyProvider.Cloudflare => ParseCloudflare(document.RootElement),
            ReverseProxyProvider.CloudFront => ParseCloudFront(document.RootElement),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown reverse proxy provider.")
        };

        // an empty list would replace the last one and leave every user behind the proxy at the proxy's address
        return ipRanges.Count > 0
            ? ipRanges
            : throw new FormatException($"The published list of {provider} has no address.");
    }

    private static IReadOnlyList<IpRange> ParseCloudflare(JsonElement root)
    {
        var result = root.GetProperty("result");
        return result.GetProperty("ipv4_cidrs").EnumerateArray()
            .Concat(result.GetProperty("ipv6_cidrs").EnumerateArray())
            .Select(ParseIpRange)
            .ToArray();
    }

    // only the addresses CloudFront connects to origins from: the rest of its list is where viewers reach it
    private static IReadOnlyList<IpRange> ParseCloudFront(JsonElement root)
    {
        return SelectOriginFacing(root, "prefixes", "ip_prefix")
            .Concat(SelectOriginFacing(root, "ipv6_prefixes", "ipv6_prefix"))
            .ToArray();
    }

    private static IEnumerable<IpRange> SelectOriginFacing(JsonElement root, string listName, string prefixName)
    {
        return root.GetProperty(listName).EnumerateArray()
            .Where(x => x.GetProperty("service").GetString() == "CLOUDFRONT_ORIGIN_FACING")
            .Select(x => ParseIpRange(x.GetProperty(prefixName)));
    }

    private static IpRange ParseIpRange(JsonElement element)
    {
        return IpRange.Parse(element.GetString() ?? throw new FormatException("The published list has a null address."));
    }
}

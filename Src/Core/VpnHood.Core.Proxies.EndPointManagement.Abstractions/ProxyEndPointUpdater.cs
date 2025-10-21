namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions;

public class ProxyEndPointUpdater
{
    public static ProxyEndPoint[] Merge(
        ProxyEndPointInfo[] currentEndPointInfos,
        ProxyEndPoint[] newEndPoints,
        int maxItemCount,
        int maxPenalty)
    {
        if (maxItemCount <= 0)
            throw new ArgumentException("MaxItemCount must be greater than 0", nameof(maxItemCount));

        // Start with new endpoints
        var result = new List<ProxyEndPoint>();

        // orders:
        // 1. previous used endpoints with penalty less or equal than maxPenalty
        // 2. new endpoints
        // 3. previous unused endpoints
        // 4. previous used endpoints with penalty greater than maxPenalty


        // Keep first maxItemCount items
        if (result.Count > maxItemCount)
            result = result.Take(maxItemCount).ToList();

        return result.ToArray();
    }

    public static async Task<ProxyEndPoint[]> UpdateFromUrlAsync(
        HttpClient httpClient,
        Uri url,
        ProxyEndPointInfo[] currentEndPointInfos,
        int maxItemCount,
        int minPenalty,
        CancellationToken cancellationToken = default)
    {
        var content = await httpClient.GetStringAsync(url, cancellationToken);
        var newProxyEndPoints = ProxyEndPointParser
            .ExtractFromContent(content)
            .Select(ProxyEndPointParser.FromUrl)
            .ToArray();
        
        // Merge with existing endpoints
        return Merge(currentEndPointInfos, newProxyEndPoints, maxItemCount, minPenalty);
    }
}

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

        // 1. previous used endpoints with penalty less or equal than maxPenalty
        var usedEndPoints = currentEndPointInfos
            .Where(info => info.Status.LastUsedTime.HasValue &&
                           info.Status.Penalty <= maxPenalty &&
                           info.EndPoint.IsEnabled)
            .OrderBy(info => info.Status.Penalty)
            .Select(info => info.EndPoint);
        result.AddRange(usedEndPoints);

        // 2. new endpoints
        AddNoDuplicate(result, newEndPoints);

        // 3. previous unused endpoints
        var unusedEndPoints = currentEndPointInfos
            .Where(info => !info.Status.LastUsedTime.HasValue && info.EndPoint.IsEnabled)
            .OrderBy(info => info.Status.Penalty)
            .Select(info => info.EndPoint);
        AddNoDuplicate(result, unusedEndPoints);

        // 4. previous used endpoints with penalty greater than maxPenalty
        var badEndPoints = currentEndPointInfos
            .Where(info => info.Status.LastUsedTime.HasValue &&
                           info.Status.Penalty > maxPenalty &&
                           info.EndPoint.IsEnabled)
            .OrderBy(info => info.Status.Penalty)
            .Select(info => info.EndPoint);
        AddNoDuplicate(result, badEndPoints);

        // 5 disabled endpoints
        var disabledEndPoints = currentEndPointInfos
            .Where(info => !info.EndPoint.IsEnabled)
            .Select(info => info.EndPoint);
        AddNoDuplicate(result, disabledEndPoints);

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

    private static void AddNoDuplicate(List<ProxyEndPoint> endPoints,
        IEnumerable<ProxyEndPoint> newEndPoints)
    {
        var existingSet = new HashSet<ProxyEndPoint>(endPoints);
        foreach (var ep in newEndPoints) {
            if (!existingSet.Contains(ep)) {
                endPoints.Add(ep);
                existingSet.Add(ep);
            }
        }
    }
}

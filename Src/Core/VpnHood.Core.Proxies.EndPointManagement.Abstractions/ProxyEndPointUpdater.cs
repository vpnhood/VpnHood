namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions;

public class ProxyEndPointUpdater
{
    public static ProxyEndPoint[] Merge(
        IEnumerable<ProxyEndPointInfo> currentEndPointInfos,
        ProxyEndPoint[] newEndPoints,
        int maxItemCount,
        int maxPenalty)
    {
        if (maxItemCount <= 0)
            throw new ArgumentException("MaxItemCount must be greater than 0", nameof(maxItemCount));

        // Start with new endpoints
        var result = new List<ProxyEndPoint>();
        var existingSet = new HashSet<ProxyEndPoint>();
        var currentEndPointInfosArray = currentEndPointInfos.ToArray();

        // 1. previous used endpoints with penalty less or equal than maxPenalty
        var usedEndPoints = currentEndPointInfosArray
            .Where(info => 
                info.Status.HasUsed &&
                info.Status.Penalty <= maxPenalty &&
                info.EndPoint.IsEnabled)
            .OrderBy(info => info.Status.Penalty)
            .Select(info => info.EndPoint);
        AddNoDuplicate(result, usedEndPoints, existingSet);

        // 2. new endpoints
        AddNoDuplicate(result, newEndPoints, existingSet);

        // 3. previous unused endpoints
        var unusedEndPoints = currentEndPointInfosArray
            .Where(info => !info.Status.HasUsed && info.EndPoint.IsEnabled)
            .OrderBy(info => info.Status.Penalty)
            .Select(info => info.EndPoint);
        AddNoDuplicate(result, unusedEndPoints, existingSet);

        // 4. previous used endpoints with penalty greater than maxPenalty
        var badEndPoints = currentEndPointInfosArray
            .Where(info => info.Status.HasUsed &&
                           info.Status.Penalty > maxPenalty &&
                           info.EndPoint.IsEnabled)
            .OrderBy(info => info.Status.Penalty)
            .Select(info => info.EndPoint);
        AddNoDuplicate(result, badEndPoints, existingSet);

        // 5 disabled endpoints
        var disabledEndPoints = currentEndPointInfosArray
            .Where(info => !info.EndPoint.IsEnabled)
            .Select(info => info.EndPoint);
        AddNoDuplicate(result, disabledEndPoints, existingSet);

        // Keep first maxItemCount items
        if (result.Count > maxItemCount)
            result = result.Take(maxItemCount).ToList();

        return result.ToArray();
    }

    public static async Task<ProxyEndPoint[]> UpdateFromUrlAsync(
        HttpClient httpClient,
        Uri url,
        IEnumerable<ProxyEndPointInfo> currentEndPointInfos,
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
        IEnumerable<ProxyEndPoint> newEndPoints,
        HashSet<ProxyEndPoint> existingSet)
    {
        foreach (var ep in newEndPoints) {
            if (!existingSet.Contains(ep)) {
                endPoints.Add(ep);
                existingSet.Add(ep);
            }
        }
    }
}

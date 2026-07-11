namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions;

public static class ProxyEndPointUpdater
{
    public const int DefaultMaxItemCount = 1000;
    public const int DefaultMaxPenalty = 50;

    public static ProxyEndPoint[] Merge(
        IEnumerable<ProxyEndPointInfo> currentEndPointInfos,
        ProxyEndPoint[] newEndPoints,
        int? maxItemCount,
        int? maxPenalty,
        bool removeDuplicateIps = false)
    {
        maxItemCount ??= DefaultMaxItemCount;
        maxPenalty ??= DefaultMaxPenalty;
        if (maxItemCount <= 0)
            throw new ArgumentException("MaxItemCount must be greater than 0", nameof(maxItemCount));

        // Start with new endpoints
        var result = new List<ProxyEndPoint>();
        var existingSet = new HashSet<ProxyEndPoint>();
        var currentEndPointInfosArray = currentEndPointInfos.ToArray();

        // remove duplicates from newEndPoints if the protocol:ip already exists in currentEndPointInfos and 
        // the current one is enabled
        if (removeDuplicateIps)
            newEndPoints = RemoveDuplicateIps(currentEndPointInfosArray, newEndPoints);

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
            result = result.Take(maxItemCount.Value).ToList();

        return result.ToArray();
    }

    private static ProxyEndPoint[] RemoveDuplicateIps(ProxyEndPointInfo[] currentEndPointInfos,
        ProxyEndPoint[] newEndPoints)
    {
        // find new endpoints that already exist in currentEndPointInfos and have succeeded before
        var alreadyExists = newEndPoints.Where(x =>
            currentEndPointInfos.Any(y =>
                y.EndPoint.Host.Equals(x.Host, StringComparison.OrdinalIgnoreCase) &&
                y.EndPoint.Protocol == x.Protocol &&
                y.Status.IsLastUsedSucceeded));

        // remove them from newEndPoints
        var filteredNewEndPoints = newEndPoints.Except(alreadyExists).ToArray();
        return filteredNewEndPoints;
    }

    public static async Task<ProxyEndPoint[]> LoadFromUrlAsync(
        HttpClient httpClient,
        Uri url,
        CancellationToken cancellationToken = default)
    {
        var content = await httpClient.GetStringAsync(url, cancellationToken);
        var proxyEndPoints = ProxyEndPointParser
            .ExtractFromContent(content)
            .Select(ProxyEndPointParser.FromUrl)
            .ToArray();

        return proxyEndPoints;
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
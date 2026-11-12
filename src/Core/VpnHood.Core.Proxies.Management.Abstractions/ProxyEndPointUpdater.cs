using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>
/// Provides the merge of a downloaded list of proxy endpoints into a managed list, and the download.
/// </summary>
public static class ProxyEndPointUpdater
{
    /// <summary>The number of endpoints a merge keeps by default: 1,000.</summary>
    public const int DefaultMaxItemCount = 1000;

    /// <summary>The penalty above which a used endpoint goes after the new ones by default: 50.</summary>
    public const int DefaultMaxPenalty = 50;

    /// <summary>
    /// Merges a new list of endpoints into the current one, in order of worth, and keeps the first ones.
    /// </summary>
    /// <param name="currentEndPointInfos">The current endpoints and their statuses.</param>
    /// <param name="newEndPoints">The new endpoints.</param>
    /// <param name="maxItemCount">How many endpoints to keep, or <see langword="null"/> for <see cref="DefaultMaxItemCount"/>.</param>
    /// <param name="maxPenalty">
    /// The penalty above which a used endpoint goes after the new ones, or <see langword="null"/> for
    /// <see cref="DefaultMaxPenalty"/>.
    /// </param>
    /// <param name="removeDuplicateIps">
    /// <see langword="true"/> to drop a new endpoint whose host, ignoring case, and protocol match a current one whose
    /// last use succeeded.
    /// </param>
    /// <returns>
    /// The merged list: the enabled current endpoints used with a penalty up to <paramref name="maxPenalty"/>, by
    /// penalty; the new ones; the enabled current ones never used; the enabled current ones above the penalty; then the
    /// disabled ones. An endpoint that comes twice as the same object is kept once; one that comes as two objects,
    /// such as a new endpoint equal to a current one, is kept twice.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="maxItemCount"/> is 0 or less.</exception>
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

        return [.. result];
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

    /// <summary>
    /// Downloads a text and reads the proxy endpoints in it, as <see cref="ProxyEndPointParser.ExtractFromContent"/>
    /// does.
    /// </summary>
    /// <param name="httpClient">The client to download with.</param>
    /// <param name="url">The text's URL.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the endpoints found.</returns>
    /// <exception cref="HttpRequestException">The download failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="NotSupportedException">An address found has a scheme that is no proxy protocol.</exception>
    public static async Task<ProxyEndPoint[]> LoadFromUrlAsync(
        HttpClient httpClient,
        Uri url,
        CancellationToken cancellationToken)
    {
        var content = await httpClient.GetStringAsync(url, cancellationToken).Vhc();
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
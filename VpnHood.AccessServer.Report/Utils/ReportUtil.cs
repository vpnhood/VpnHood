namespace VpnHood.AccessServer.Report.Utils;

public static class ReportUtil
{
    public static string? GenerateCacheKey(string keyBase, DateTime? beginTime, DateTime? endTime,
        out TimeSpan? cacheExpiration)
    {
        cacheExpiration = null;
        if (endTime != null && DateTime.UtcNow - endTime >= TimeSpan.FromMinutes(5))
            return null;

        var duration = (endTime ?? DateTime.UtcNow) - (beginTime ?? DateTime.UtcNow.AddYears(-2));
        var threshold = (long)(duration.TotalMinutes / 30);
        var cacheKey = $"{keyBase}_{threshold}";
        cacheExpiration = TimeSpan.FromMinutes(Math.Min(60 * 24, duration.TotalMinutes / 30));
        return cacheKey;
    }
}
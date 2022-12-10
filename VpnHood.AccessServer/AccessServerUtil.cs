using System;
using System.Linq;
using System.Net;

namespace VpnHood.AccessServer;

public static class AccessUtil
{
    public static string ValidateIpEndPoint(string ipEndPoint)
    {
        return IPEndPoint.Parse(ipEndPoint).ToString();
    }
        
    public static string ValidateIpAddress(string ipAddress)
    {
        return IPAddress.Parse(ipAddress).ToString();
    }

    public static string FindUniqueName(string template, string?[] names)
    {
        for (var i = 2; ; i++)
        {
            var name = template.Replace("##", i.ToString());
            if (names.All(x => x != name))
                return name;
        }
    }

    public static string? GenerateCacheKey(string keyBase, DateTime? startTime, DateTime? endTime, out TimeSpan? cacheExpiration)
    {
        cacheExpiration = null;
        if (endTime != null && DateTime.UtcNow - endTime >= TimeSpan.FromMinutes(5)) 
            return null;

        var duration = (endTime ?? DateTime.UtcNow) - (startTime ?? DateTime.UtcNow.AddYears(-2));
        var threshold = (long) (duration.TotalMinutes / 30);
        var cacheKey = $"{keyBase}_{threshold}";
        cacheExpiration = TimeSpan.FromMinutes(Math.Min(60 * 24, duration.TotalMinutes / 30));
        return cacheKey;
    }
}
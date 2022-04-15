using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Data.SqlClient;
using VpnHood.AccessServer.Exceptions;

namespace VpnHood.AccessServer;

public static class AccessUtil
{
    // 2601 unique index
    // 2627 PRIMARY KEY duplicate
    public static bool IsAlreadyExistsException(Exception ex)
    {
        return ex is AlreadyExistsException ||
               ex.InnerException is SqlException {Number: 2601 or 2627} ||
               ex is SqlException {Number: 2601 or 2627};
    }

    public static bool IsNotExistsException(Exception ex)
    {
        return ex is KeyNotFoundException ||
               ex is InvalidOperationException && ex.Message.Contains("Sequence contains no elements");
    }

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
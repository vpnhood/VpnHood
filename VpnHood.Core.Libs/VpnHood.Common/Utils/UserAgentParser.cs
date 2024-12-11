﻿namespace VpnHood.Common.Utils;

public static class UserAgentParser
{
    // <summary>
    // Extracts human-readable Operating system name.
    // </summary>
    // <param name="userAgent">User Agent string from Request.</param>
    // <returns>Human-readable Operating system name.</returns>
    public static string GetOperatingSystem(string? userAgent)
    {
        userAgent ??= string.Empty;

        string clientOsName;
        if (userAgent.Contains("Windows 98"))
            clientOsName = "Windows 98";

        else if (userAgent.Contains("Windows NT 5.0"))
            clientOsName = "Windows 2000";

        else if (userAgent.Contains("Windows NT 5.1"))
            clientOsName = "Windows XP";

        else if (userAgent.Contains("Windows NT 6.0"))
            clientOsName = "Windows Vista";

        else if (userAgent.Contains("Windows NT 6.1"))
            clientOsName = "Windows 7";

        else if (userAgent.Contains("Windows NT 6.2"))
            clientOsName = "Windows 8";

        else if (userAgent.Contains("Windows"))
            clientOsName = GetOsVersion(userAgent, "Windows");

        else if (userAgent.Contains("Android"))
            clientOsName = GetOsVersion(userAgent, "Android");

        else if (userAgent.Contains("Linux"))
            clientOsName = GetOsVersion(userAgent, "Linux");

        else if (userAgent.Contains("iPhone"))
            clientOsName = GetOsVersion(userAgent, "iPhone");

        else if (userAgent.Contains("iPad"))
            clientOsName = GetOsVersion(userAgent, "iPad");

        else if (userAgent.Contains("Macintosh"))
            clientOsName = GetOsVersion(userAgent, "Macintosh");

        else
            clientOsName = "Unknown OS";

        return clientOsName;
    }

    private static string GetOsVersion(string userAgent, string osName)
    {
        return userAgent.Split([osName], StringSplitOptions.None)[1].Split(';', ')').Length != 0
            ? $"{osName}{userAgent.Split([osName], StringSplitOptions.None)[1].Split(';', ')')[0]}"
            : osName;
    }
}
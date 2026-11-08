namespace VpnHood.Net.Toolkit.Utils;

/// <summary>
/// Provides the means to read the operating system from a browser's User-Agent.
/// </summary>
public static class UserAgentParser
{
    /// <summary>Reads the operating system's name by a heuristic, with a version where it finds one.</summary>
    /// <param name="userAgent">The User-Agent, or <see langword="null"/>.</param>
    /// <returns>The name, such as <c>Windows 7</c>, or <c>Unknown OS</c>.</returns>
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
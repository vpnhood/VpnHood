using System;

namespace VpnHood.Server.SystemInformation;

public class BasicSystemInfoProvider : ISystemInfoProvider
{
    public string GetOperatingSystemInfo()
    {
        return Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
    }

    public SystemInfo GetSystemInfo()
    {
        return new SystemInfo(null, null, null);
    }
}
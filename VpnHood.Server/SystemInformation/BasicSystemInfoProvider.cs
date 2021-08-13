using System;

namespace VpnHood.Server.SystemInformation
{
    public class BasicSystemInfoProvider : ISystemInfoProvider
    {
        public string GetOperatingSystemInfo()
            => Environment.OSVersion.ToString() + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");

        public SystemInfo GetSystemInfo()
            => new (0, 0);
    }

}

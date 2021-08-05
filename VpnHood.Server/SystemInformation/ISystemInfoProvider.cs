namespace VpnHood.Server.SystemInformation
{
    public interface ISystemInfoProvider
    {
        SystemInfo GetSystemInfo();
        string GetOperatingSystemInfo();
    }
}

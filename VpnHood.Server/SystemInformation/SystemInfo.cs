namespace VpnHood.Server.SystemInformation
{
    public class SystemInfo
    {
        public SystemInfo(long totalMemory, long freeMemory)
        {
            TotalMemory = totalMemory;
            FreeMemory = freeMemory;
        }

        public long TotalMemory { get; }
        public long FreeMemory { get; }
    }
}

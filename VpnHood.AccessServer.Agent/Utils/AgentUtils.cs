namespace VpnHood.AccessServer.Agent.Utils;

public static class AgentUtils
{
    public static int GetBestTcpBufferSize(long? totalMemory)
    {
        if (totalMemory == null)
            return 8192;

        var bufferSize = (long)Math.Round((double)totalMemory / 0x80000000) * 4096;
        bufferSize = Math.Max(bufferSize, 8192);
        bufferSize = Math.Min(bufferSize, 8192); //81920, it looks it doesn't have effect
        return (int)bufferSize;
    }
}
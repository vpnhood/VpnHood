using System;

namespace VpnHood.Server;

public static class AccessUtil
{
    public static int GetBestTcpBufferSize(long totalMemory)
    {
        var bufferSize = (long)Math.Round((double)totalMemory / 0x80000000) * 4096;
        bufferSize = Math.Max(bufferSize, 8192);
        bufferSize = Math.Min(bufferSize, 81920);
        return (int)bufferSize;
    }

}
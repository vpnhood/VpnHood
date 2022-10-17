using System;

namespace VpnHood.AccessServer.Dtos;

public class ServerUsage
{
    public DateTime Time { get; set; }
    public int SessionCount { get; set; }
    public long TunnelTransferSpeed { get; set; }
    public int ServerCount { get; set; }
}
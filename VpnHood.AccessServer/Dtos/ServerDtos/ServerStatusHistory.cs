using System;

namespace VpnHood.AccessServer.Dtos.ServerDtos;

public class ServerStatusHistory
{
    public DateTime Time { get; set; }
    public int SessionCount { get; set; }
    public long TunnelTransferSpeed { get; set; }
    public int ServerCount { get; set; }
}
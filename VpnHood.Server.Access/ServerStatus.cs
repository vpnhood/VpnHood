﻿using VpnHood.Common.Messaging;

namespace VpnHood.Server.Access;

public class ServerStatus
{
    public int SessionCount { get; set; }
    public int TcpConnectionCount { get; set; }
    public int UdpConnectionCount { get; set; }
    public long? AvailableMemory { get; set; }
    public int? CpuUsage { get; set; }
    public long UsedMemory { get; set; }
    public int ThreadCount { get; set; }
    public Traffic TunnelSpeed { get; set; } = new();
    public string? ConfigCode { get; set; }

    // todo 22
}
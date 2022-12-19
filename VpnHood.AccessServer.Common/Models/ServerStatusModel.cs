﻿namespace VpnHood.AccessServer.Models;

public class ServerStatusModel
{
    public long ServerStatusId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ServerId { get; set; }
    public int SessionCount { get; set; }
    public int TcpConnectionCount { get; set; }
    public int UdpConnectionCount { get; set; }
    public long? AvailableMemory { get; set; }
    public byte? CpuUsage { get; set; }
    public int ThreadCount { get; set; }
    public long TunnelSendSpeed { get; set; }
    public long TunnelReceiveSpeed { get; set; }
    public bool IsConfigure { get; set; }
    public DateTime CreatedTime { get; set; }
    public bool IsLast { get; set; }

    public virtual ProjectModel? Project { get; set; }
    public virtual ServerModel? Server { get; set; }
}
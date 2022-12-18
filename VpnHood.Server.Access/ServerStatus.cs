namespace VpnHood.Server;

public class ServerStatus
{
    public int SessionCount { get; set; }
    public int TcpConnectionCount { get; set; }
    public int UdpConnectionCount { get; set; }
    public long? AvailableMemory { get; set; }
    public int? CpuUsage { get; set; }
    public long UsedMemory { get; set; }
    public int ThreadCount { get; set; }
    public long TunnelSendSpeed { get; set; }
    public long TunnelReceiveSpeed { get; set; }
    public string? ConfigCode { get; set; }
}
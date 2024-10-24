namespace VpnHood.AccessServer.Dtos.Servers;

public class ServerStatusEx
{
    public required int SessionCount { get; init; }
    public required int TcpConnectionCount { get; init; }
    public required int UdpConnectionCount { get; init; }
    public required long? AvailableMemory { get; init; }
    public required long? AvailableSwapMemoryMb { get; init; }
    public required int? CpuUsage { get; init; }
    public required int ThreadCount { get; init; }
    public required long TunnelSendSpeed { get; init; }
    public required long TunnelReceiveSpeed { get; init; }
    public required DateTime CreatedTime { get; init; }
}
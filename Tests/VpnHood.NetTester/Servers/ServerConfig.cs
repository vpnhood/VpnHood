namespace VpnHood.NetTester.Servers;

internal class ServerConfig
{
    public int TcpPort { get; init; }
    public int HttpPort { get; init; }
    public int QuicPort { get; init; }
    public int HttpsPort { get; init; }
    public required string Domain { get; init; }
    public bool IsValidDomain { get; init; }
}
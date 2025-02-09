namespace VpnHood.Core.Server;

internal class SessionLocalData
{
    public required ulong SessionId { get; init; }
    public required int ProtocolVersion { get; init; }
    public required VirtualIpBundle VirtualIps { get; init; }
}
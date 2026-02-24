using System.Net;
using VpnHood.Core.Client.Abstractions;

namespace VpnHood.Core.Client;

internal class ClientSessionOptions
{
    public required IPAddress TcpProxyCatcherAddressIpV4 { get; init; }
    public required IPAddress TcpProxyCatcherAddressIpV6 { get; init; }
    public required ChannelProtocol ChannelProtocol { get; init; }
    public required bool DropUdp { get; init; }
    public required bool DropQuic { get; init; }
    public required bool UseTcpProxy { get; init; }
}
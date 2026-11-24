using System.Net.Sockets;
using VpnHood.Core.Proxies.Management;

namespace VpnHood.Test.Providers.Proxies;

public sealed class ConnectionThroughProxy(ManagedProxyConnector connector, TcpClient tcpClient)
{
    public ManagedProxyConnector Connector { get; } = connector;
    public TcpClient TcpClient { get; } = tcpClient;
}

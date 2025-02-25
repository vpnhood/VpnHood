using System.Net.Sockets;

namespace VpnHood.Core.Tunneling.Sockets;

public interface ISocketFactory
{
    public TcpClient CreateTcpClient(AddressFamily addressFamily);
    public UdpClient CreateUdpClient(AddressFamily addressFamily);
    public void SetKeepAlive(Socket socket, bool enable);
}
using System.Net;
using System.Net.Sockets;

namespace VpnHood.Core.Toolkit.Sockets;

public interface ISocketFactory
{
    public TcpClient CreateTcpClient(IPEndPoint ipEndPoint);
    public UdpClient CreateUdpClient(AddressFamily addressFamily);
    public void SetKeepAlive(Socket socket, bool enable);
}
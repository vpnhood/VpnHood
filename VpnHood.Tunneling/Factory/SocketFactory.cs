using System.Net;
using System.Net.Sockets;

namespace VpnHood.Tunneling.Factory
{
    public class SocketFactory
    {
        public virtual TcpClient CreateTcpClient()
        {
            return new TcpClient(new IPEndPoint(IPAddress.Any, 0));
        }

        public virtual UdpClient CreateUdpClient()
        {
            return new UdpClient(0);
        }
    }
}
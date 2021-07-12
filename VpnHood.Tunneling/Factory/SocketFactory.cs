using System.Net;
using System.Net.Sockets;

namespace VpnHood.Tunneling.Factory
{
    public class SocketFactory
    {
        public virtual TcpClient CreateTcpClient() => new ();
        public virtual UdpClient CreateUdpClient() => new ();
    }
}



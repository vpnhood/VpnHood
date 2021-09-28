using System.Net;
using System.Net.Sockets;
using VpnHood.Common;

namespace VpnHood.Tunneling.Factory
{
    public class SocketFactory
    {
        public virtual TcpClient CreateTcpClient(AddressFamily addressFamily)
        {
            var anyIpAddress = Util.GetAnyIpAddress(addressFamily);
            return new TcpClient(new IPEndPoint(anyIpAddress, 0));
        }

        public virtual UdpClient CreateUdpClient(AddressFamily addressFamily)
        {
            return new UdpClient(0, addressFamily);
        }
    }
}
using System.Net;
using System.Net.Sockets;
using VpnHood.Common.Net;

namespace VpnHood.Tunneling.Factory
{
    public class SocketFactory
    {
        public virtual TcpClient CreateTcpClient(AddressFamily addressFamily)
        {
            var anyIpAddress = IPAddressUtil.GetAnyIpAddress(addressFamily);
            return new TcpClient(new IPEndPoint(anyIpAddress, 0));
        }

        public virtual UdpClient CreateUdpClient(AddressFamily addressFamily)
        {
            return new UdpClient(0, addressFamily);
        }
    }
}
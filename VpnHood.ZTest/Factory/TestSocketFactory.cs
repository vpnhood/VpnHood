using System;
using System.Net;
using System.Net.Sockets;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Test.Factory
{
    public class TestSocketFactory : SocketFactory
    {
        public override TcpClient CreateTcpClient()
        {
            for (var i = TestPacketCapture.ServerMinPort; i <= TestPacketCapture.ServerMaxPort; i++)
            {
                try
                {
                    var localEndPoint = new IPEndPoint(IPAddress.Any, i);
                    var tcpClient = new TcpClient(localEndPoint);
                    return tcpClient;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    // try next
                }
            }

            throw new Exception("Could not find free port for test!");
        }

        public override UdpClient CreateUdpClient()
        {
            for (var i = TestPacketCapture.ServerMinPort; i <= TestPacketCapture.ServerMaxPort; i++)
            {
                try
                {
                    var localEndPoint = new IPEndPoint(IPAddress.Any, i);
                    var tcpClient = new UdpClient(localEndPoint);
                    return tcpClient;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    // try next
                }
            }

            throw new Exception("Could not find free port for test!");
        }
    }
}

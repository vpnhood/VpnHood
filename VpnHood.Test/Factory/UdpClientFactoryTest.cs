using VpnHood.Server.Factory;
using System;
using System.Net;
using System.Net.Sockets;

namespace VpnHood.Test.Factory
{
    public class UdpClientFactoryTest : UdpClientFactory
    {
        public override UdpClient CreateListner()
        {
            for (var i = WinDivertDeviceTest.ServerMinPort; i <= WinDivertDeviceTest.ServerMaxPort; i++)
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

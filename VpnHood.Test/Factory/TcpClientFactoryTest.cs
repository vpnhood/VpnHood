using VpnHood.Server;
using VpnHood.Server.Factory;
using System;
using System.Net;
using System.Net.Sockets;

namespace VpnHood.Test.Factory
{
    public class TcpClientFactoryTest : TcpClientFactory
    {
        public override TcpClient CreateAndConnect(IPEndPoint remoteEP)
        {
            for (var i=WinDivertDeviceTest.ServerMinPort; i<= WinDivertDeviceTest.ServerMaxPort; i++ )
            {
                try
                {
                    var localEndPoint = new IPEndPoint(IPAddress.Any, i);
                    var tcpClient = new TcpClient(localEndPoint) { NoDelay = true };
                    tcpClient.Connect(remoteEP);
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

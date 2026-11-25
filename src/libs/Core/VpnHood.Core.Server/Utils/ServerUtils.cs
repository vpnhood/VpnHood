using System.Net;
using System.Net.Sockets;

namespace VpnHood.Core.Server.Utils;

internal static class ServerUtils
{
    public static int GetFreeUdpPort(AddressFamily addressFamily, int? start)
    {
        start ??= new Random().Next(1024, 9000);
        for (var port = start.Value; port < start + 1000; port++) {
            try {
                using var udpClient = new UdpClient(port, addressFamily);
                var localEndPoint = (IPEndPoint?)udpClient.Client.LocalEndPoint ??
                                    throw new InvalidOperationException("LocalEndPoint is null");
                return localEndPoint.Port;
            }
            catch (SocketException ex) when (ex.SocketErrorCode != SocketError.AddressAlreadyInUse) {
                break;
            }
            catch {
                // try the next port but more
                if (start < 1024) {
                    start = 1024;
                    port = 1023; // will be incremented to 1024 in the next loop
                }
            }
        }

        // try any port
        try {
            using var udpClient = new UdpClient(0, addressFamily);
            var localEndPoint = (IPEndPoint?)udpClient.Client.LocalEndPoint ??
                                throw new InvalidOperationException("LocalEndPoint is null");
            return localEndPoint.Port;
        }
        catch {
            return 0;
        }
    }
}
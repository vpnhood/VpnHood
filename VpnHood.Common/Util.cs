using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace VpnHood.Common
{
    public static class Util
    {
        public static  bool TryParseIpEndPoint(string value, out IPEndPoint ipEndPoint)
        {
            ipEndPoint = null;
            var addr = value.Split(':');
            if (addr.Length != 2) return false;
            if (!IPAddress.TryParse(addr[0], out IPAddress ipAddress)) return false;
            if (!int.TryParse(addr[1], out int port)) return false;
            ipEndPoint = new IPEndPoint(ipAddress, port);
            return true;
        }

        public static IPEndPoint ParseIpEndPoint(string value)
        {
            if (!TryParseIpEndPoint(value, out IPEndPoint ipEndPoint))
                throw new ArgumentException($"Could not parse {value} to an IpEndPoint");
            return ipEndPoint;
        }

        public static IPAddress GetLocalIpAddress()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 0);
            var endPoint = (IPEndPoint)socket.LocalEndPoint;
            return endPoint.Address;
        }

        public static bool IsSocketClosedException(Exception ex)
        {
            return ex is ObjectDisposedException || ex is IOException || ex is SocketException;
        }
    }
}

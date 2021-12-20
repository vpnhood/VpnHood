using System;

namespace VpnHood.Tunneling
{
    public static class TunnelUtil
    {
        public const int MaxDatagramChannelCount = 8;
        public const int StreamBufferSize = 8192; //default: 0x14000
        public const int TlsHandshakeLength = 5000;
        public const int MtuWithFragmentation = 0xFFFF - 70;
        public const int MtuWithoutFragmentation = 1500 - 70;
        public static TimeSpan TcpTimeout = TimeSpan.FromMinutes(15);
    }
}
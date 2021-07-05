namespace VpnHood.Tunneling
{
    public static class TunnelUtil
    {
        public const int SocketStackSize_Stream = 0xFFFF * 2;
        public const int SocketStackSize_Datagram = 0xFFFF * 2;
        public const int MaxDatagramChannelCount = 8;
        public const int TlsHandshakeLength = 5000;
        public const int MtuWithFragmentation = 0xFFFF - 70;
        public const int MtuWithoutFragmentation = 1500 - 70;
    }
}

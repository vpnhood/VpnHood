using System;

namespace VpnHood.Tunneling;

public static class TunnelDefaults
{
    public const int MaxDatagramChannelCount = 8;
    public const int TlsHandshakeLength = 5000;
    public const int MtuWithFragmentation = 0xFFFF - 70;
    public const int MtuWithoutFragmentation = 1500 - 70;
    public const string HttpPassCheck = "VpnHoodPassCheck";
    public static TimeSpan TcpCheckInterval { get; set; } = TimeSpan.FromMinutes(15);
    public static TimeSpan TcpGracefulTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public static TimeSpan TcpReuseTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public static TimeSpan TcpRequestTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public static int TcpProxyEncryptChunkCount { get; set; } = 4;
}
using System;

namespace VpnHood.Tunneling;

public static class TunnelDefaults
{
    public const int MaxDatagramChannelCount = 8;
    public const int TlsHandshakeLength = 5000;
    public const int MtuWithFragmentation = 0xFFFF - 70;
    public const int MtuWithoutFragmentation = 1500 - 70;
    public static TimeSpan TcpTimeout { get; set; } = TimeSpan.FromMinutes(15);
    public static TimeSpan TcpGracefulTimeout { get; set; } = TimeSpan.FromSeconds(10); //todo must be 60
}
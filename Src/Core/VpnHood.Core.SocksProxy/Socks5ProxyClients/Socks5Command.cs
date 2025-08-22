namespace VpnHood.Core.SocksProxy.Socks5ProxyClients;

public enum Socks5Command : byte
{
    Connect = 0x01,
    Bind = 0x02,
    UdpAssociate = 0x03
}
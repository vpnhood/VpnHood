namespace VpnHood.Core.Tunneling.Socks5Proxy;

public enum Socks5AddressType : byte
{
    IpV4 = 0x01,
    DomainName = 0x03,
    IpV6 = 0x04
}
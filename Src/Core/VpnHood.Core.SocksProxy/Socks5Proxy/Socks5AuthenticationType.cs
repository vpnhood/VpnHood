namespace VpnHood.Core.SocksProxy.Socks5Proxy;

public enum Socks5AuthenticationType : byte
{
    NoAuthenticationRequired = 0x00,
    Gssapi = 0x01,
    UsernamePassword = 0x02,
    IanaAssignedRangeBegin = 0x03,
    IanaAssignedRangeEnd = 0x7f,
    ReservedRangeBegin = 0x80,
    ReservedRangeEnd = 0xfe,
    ReplyNoAcceptableMethods = 0xff
}
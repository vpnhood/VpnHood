namespace VpnHood.Core.SocksProxy.Socks4ProxyClients;

// ReSharper disable IdentifierTypo
public enum Socks4ReplyCode : byte
{
    RequestGranted = 0x5A,
    RequestRejectedOrFailed = 0x5B,
    CannotConnectToIdentd = 0x5C,
    DifferingUserId = 0x5D
}

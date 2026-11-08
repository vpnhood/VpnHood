namespace VpnHood.Core.Client.Abstractions.Exceptions;

// The server's name no longer gives the address the session was fixed to: a server behind a dynamic name (DDNS) has
// moved. A new connect looks the name up again, which a VPN service that can connect by itself does at once, with the
// name's new addresses: none where the error came from another process.
public class ServerAddressChangedException(
    string message,
    HostNameAddresses? newAddresses = null,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public HostNameAddresses? NewAddresses { get; } = newAddresses;
}

namespace VpnHood.Core.Client.Abstractions.Exceptions;

/// <summary>
/// The exception that is thrown when a session ends because the server's host name no longer gives the address the
/// session was made to, as when a server behind a dynamic name moved.
/// </summary>
/// <param name="message">The message.</param>
/// <param name="newAddresses">The name's new addresses, or <see langword="null"/>.</param>
/// <param name="innerException">The exception that caused this one, or <see langword="null"/>.</param>
/// <remarks>
/// A VPN service that can connect by itself connects again at once, taking <see cref="NewAddresses"/> rather than
/// asking the name; another connection asks the name anew.
/// </remarks>
public class ServerAddressChangedException(
    string message,
    HostNameAddresses? newAddresses = null,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>
    /// Gets the name's new addresses, for the next connection to take; <see langword="null"/> where the error came from
    /// another process.
    /// </summary>
    public HostNameAddresses? NewAddresses { get; } = newAddresses;
}

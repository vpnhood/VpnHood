namespace VpnHood.Core.Client.VpnServices.Abstractions.Messaging;

/// <summary>
/// Defines the app's side of the channel to the VPN service: it sends a request and gets the answer.
/// </summary>
public interface IMessageClient : IDisposable
{
    /// <summary>Sends a request and returns the answer.</summary>
    /// <param name="request">The request's bytes.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the answer's bytes.</returns>
    /// <remarks>The transport owns making the connection, keeping it for reuse, and connecting again.</remarks>
    Task<Memory<byte>> SendAsync(Memory<byte> request, CancellationToken cancellationToken);
}

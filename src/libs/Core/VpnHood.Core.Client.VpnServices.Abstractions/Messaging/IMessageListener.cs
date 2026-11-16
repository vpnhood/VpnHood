namespace VpnHood.Core.Client.VpnServices.Abstractions.Messaging;

/// <summary>
/// Defines the VPN service's side of the channel to the app: it hands each request to a handler and sends its answer
/// back.
/// </summary>
public interface IMessageListener : IDisposable
{
    /// <summary>Starts listening: each request goes to the handler, and the bytes it returns are the answer.</summary>
    /// <param name="messageHandler">The handler.</param>
    /// <param name="cancellationToken">The token that stops listening, for a transport that owns a loop.</param>
    /// <returns>A task that completes when listening stops, or, for a transport with no loop, once it has begun.</returns>
    /// <remarks>
    /// The transport owns connections, accepting them and, where it has any, authentication. A transport without a loop
    /// stops on <see cref="IDisposable.Dispose"/>, which the caller must call either way.
    /// </remarks>
    Task Start(MessageHandler messageHandler, CancellationToken cancellationToken);
}

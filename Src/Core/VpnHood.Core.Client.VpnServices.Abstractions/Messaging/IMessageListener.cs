namespace VpnHood.Core.Client.VpnServices.Abstractions.Messaging;

public interface IMessageListener : IDisposable
{
    // Start listening for incoming messages. Each received message is passed to the handler
    // and the returned blob is sent back as the response. The transport owns all connection,
    // accept and (if applicable) authentication concerns.
    Task Start(MessageHandler messageHandler, CancellationToken cancellationToken);
}

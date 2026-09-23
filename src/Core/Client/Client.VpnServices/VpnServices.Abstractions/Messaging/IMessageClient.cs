namespace VpnHood.Core.Client.VpnServices.Abstractions.Messaging;

public interface IMessageClient : IDisposable
{
    // Send a request blob and return the response blob. The transport owns connection
    // establishment, persistent reuse and transparent reconnection.
    Task<Memory<byte>> SendAsync(Memory<byte> request, CancellationToken cancellationToken);
}

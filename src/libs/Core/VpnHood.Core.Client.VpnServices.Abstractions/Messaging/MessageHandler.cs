namespace VpnHood.Core.Client.VpnServices.Abstractions.Messaging;

/// <summary>Represents the method that answers a request from the app.</summary>
/// <param name="request">The request's bytes.</param>
/// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
/// <returns>A task whose result is the answer's bytes.</returns>
public delegate Task<Memory<byte>> MessageHandler(Memory<byte> request, CancellationToken cancellationToken);
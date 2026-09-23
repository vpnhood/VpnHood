namespace VpnHood.Core.Client.VpnServices.Abstractions.Messaging;

public delegate Task<Memory<byte>> MessageHandler(Memory<byte> request, CancellationToken cancellationToken);
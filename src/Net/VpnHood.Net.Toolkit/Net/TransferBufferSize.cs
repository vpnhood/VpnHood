namespace VpnHood.Net.Toolkit.Net;

/// <summary>Represents a pair of buffer sizes, for sending and for receiving.</summary>
/// <param name="Send">The send buffer's size, in bytes.</param>
/// <param name="Receive">The receive buffer's size, in bytes.</param>
public readonly record struct TransferBufferSize(int Send, int Receive);

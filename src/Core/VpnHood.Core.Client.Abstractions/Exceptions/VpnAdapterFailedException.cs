namespace VpnHood.Core.Client.Abstractions.Exceptions;

// The VPN adapter stopped on its own (IVpnAdapter.Failed): its interface disabled or gone, its I/O broken, or
// its restart failed. A start that fails is not this: it fails the connect with its own error.
public class VpnAdapterFailedException(string message, Exception? innerException = null)
    : Exception(message, innerException);

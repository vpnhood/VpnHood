namespace VpnHood.Core.Server.Listeners.Http3;

// Internal control flow: the application can finish its response without waiting for more body bytes.
internal sealed class Http3ReadPendingException : Exception;

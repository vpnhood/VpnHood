namespace VpnHood.Core.Server.ProbeResistance;

// A first plaintext request containing HTTP/2's exact opening line gets a binary GOAWAY, not an HTTP error.
internal sealed class Http2PrefaceException : Exception;

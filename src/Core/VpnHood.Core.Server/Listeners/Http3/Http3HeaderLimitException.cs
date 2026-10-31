namespace VpnHood.Net.Http3;

// A well-formed compressed block can exceed HTTP's limits; that is not a QPACK decompression error.
internal sealed class Http3HeaderLimitException(string message) : Exception(message);

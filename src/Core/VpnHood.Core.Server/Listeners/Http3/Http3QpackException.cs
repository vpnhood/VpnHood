namespace VpnHood.Net.Http3;

internal sealed class Http3QpackException(string message) : Exception(message)
{
    public long ErrorCode => 0x200; // QPACK_DECOMPRESSION_FAILED.
}

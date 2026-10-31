namespace VpnHood.Net.Http3;

internal sealed class Http3ProtocolException(long errorCode, bool connectionError = true)
    : Exception($"HTTP/3 protocol error 0x{errorCode:x}.")
{
    public long ErrorCode { get; } = errorCode;
    public bool ConnectionError { get; } = connectionError;
}

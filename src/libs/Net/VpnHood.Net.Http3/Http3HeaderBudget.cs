namespace VpnHood.Net.Http3;

// Headers and trailers share Kestrel's eager parsing allowance.
internal sealed class Http3HeaderBudget(Http3ServerOptions options)
{
    private long _bytes;
    private long _count;

    public bool ExceedsRequestLimits => Volatile.Read(ref _bytes) > options.MaxHeaderBytes ||
                                        Volatile.Read(ref _count) > options.MaxHeaderCount;

    public void Append(KeyValuePair<string, string> field)
    {
        // Kestrel counts name plus value, without the 32 bytes per field of HTTP/3's field-section size.
        var bytes = _bytes + field.Key.Length + field.Value.Length;
        Volatile.Write(ref _bytes, bytes);
        if (bytes > (long)options.MaxHeaderBytes * 2)
            throw new Http3ProtocolException(Http3Protocol.RequestRejected, connectionError: false);
        var count = _count + 1;
        Volatile.Write(ref _count, count);
        if (count > (long)options.MaxHeaderCount * 2)
            throw new Http3ProtocolException(Http3Protocol.MessageError, connectionError: false);
    }
}

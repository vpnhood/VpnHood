namespace VpnHood.Net.Http3;

/// <summary>
/// Provides the HTTP/3 error codes (RFC 9114, section 8.1) a server closes a connection with, through
/// <see cref="Http3ServerConnection.Close"/>.
/// </summary>
public static class Http3ErrorCode
{
    /// <summary>H3_NO_ERROR: no error; the connection closes without one.</summary>
    public const long NoError = 0x100;

    /// <summary>H3_INTERNAL_ERROR: an internal error in the HTTP stack.</summary>
    public const long InternalError = 0x102;

    /// <summary>H3_EXCESSIVE_LOAD: the peer's behavior might be generating excessive load.</summary>
    public const long ExcessiveLoad = 0x107;
}

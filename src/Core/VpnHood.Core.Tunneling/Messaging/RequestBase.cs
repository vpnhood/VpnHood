using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Tunneling.Messaging;

/// <summary>Provides the base of a request in a session, which carries the session's ID and key.</summary>
/// <param name="requestCode">The code of the request's kind.</param>
public abstract class RequestBase(RequestCode requestCode)
    : ClientRequest((byte)requestCode)
{
    /// <summary>Gets or sets the session's ID.</summary>
    public required ulong SessionId { get; set; }

    /// <summary>Gets or sets the session's key, which shows the request is the session's.</summary>
    public required ReadOnlyMemory<byte> SessionKey { get; set; }
}
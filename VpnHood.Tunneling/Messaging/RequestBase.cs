using VpnHood.Common.Messaging;

namespace VpnHood.Tunneling.Messaging;

public abstract class RequestBase(RequestCode requestCode, string requestId, ulong sessionId, byte[] sessionKey)
    : ClientRequest((byte)requestCode, requestId)
{
    public ulong SessionId { get; set; } = sessionId;
    public byte[] SessionKey { get; set; } = sessionKey ?? throw new ArgumentNullException(nameof(sessionKey));
}
using VpnHood.Common.Messaging;

namespace VpnHood.Tunneling.Messaging;

public abstract class RequestBase : ClientRequest
{
    protected RequestBase(RequestCode requestCode, string requestId, ulong sessionId, byte[] sessionKey)
        :base((byte)requestCode, requestId)
    {
        SessionId = sessionId;
        SessionKey = sessionKey ?? throw new ArgumentNullException(nameof(sessionKey));
    }

    public ulong SessionId { get; set; }
    public byte[] SessionKey { get; set; }
}
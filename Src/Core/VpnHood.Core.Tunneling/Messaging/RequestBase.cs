using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Tunneling.Messaging;

public abstract class RequestBase(RequestCode requestCode)
    : ClientRequest((byte)requestCode)
{
    public required ulong SessionId { get; set; }
    public required byte[] SessionKey { get; set; }
}
namespace VpnHood.Common.Messaging;

public abstract class ClientRequest(byte requestCode, string requestId)
{
    public byte RequestCode { get; } = requestCode;
    public string RequestId { get; set; } = requestId;

    protected ClientRequest(ClientRequest obj) : this(obj.RequestCode, obj.RequestId)
    {
    }
}
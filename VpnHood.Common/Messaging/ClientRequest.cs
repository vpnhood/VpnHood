namespace VpnHood.Common.Messaging;

public abstract class ClientRequest
{
    public byte RequestCode { get; }
    public string RequestId { get; set; }

    protected ClientRequest(byte requestCode, string requestId)
    {
        RequestCode = requestCode;
        RequestId = string.IsNullOrEmpty(requestId) ? "OldVersion" : requestId; //must be required after >= 3.0.371
    }

    protected ClientRequest(ClientRequest obj)
    {
        RequestCode = obj.RequestCode;
        RequestId = obj.RequestId;
    }
}
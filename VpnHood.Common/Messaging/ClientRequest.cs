using System.Text.Json.Serialization;

namespace VpnHood.Common.Messaging;

public abstract class ClientRequest
{
    [JsonIgnore]
    public byte RequestCode { get; }
    public string RequestId { get; }

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
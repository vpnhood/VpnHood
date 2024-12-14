namespace VpnHood.Core.Common.Messaging;

public abstract class ClientRequest(byte requestCode)
{
    public byte RequestCode { get; } = requestCode;
    public required string RequestId { get; set; }
}
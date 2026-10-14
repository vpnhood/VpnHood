namespace VpnHood.Core.Common.Messaging;

public abstract class ClientRequest(byte requestCode)
{
    public byte RequestCode { get; } = requestCode;
    public required string RequestId { get; set; }
    // init, so the server reads the client's own time rather than stamping the moment it reads the request
    public DateTime RequestTime { get; init; } = DateTime.UtcNow;
}
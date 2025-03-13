namespace VpnHood.Core.Client.Abstractions;

public class AdRequest
{
    public required AdRequestType AdRequestType { get; init; }
    public required string SessionId { get; init; }
    public required Guid RequestId { get; init; }
}
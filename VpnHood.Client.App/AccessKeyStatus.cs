namespace VpnHood.Client.App;

public class AccessKeyStatus
{
    public required string? Name { get; init; }
    public required string? SupportId { get; init; }
    public required ClientProfile? ClientProfile { get; init; }
}
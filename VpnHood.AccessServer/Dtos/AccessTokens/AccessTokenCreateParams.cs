using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Dtos.AccessTokens;
public class AccessTokenCreateParams
{
    public Guid? AccessTokenId { get; init; }
    public Guid ServerFarmId { get; init; }
    public string? AccessTokenName { get; init; }
    public byte[]? Secret { get; init; }
    public long MaxTraffic { get; init; }
    public int Lifetime { get; init; }
    public int MaxDevice { get; init; }
    public DateTime? ExpirationTime { get; init; }
    public bool? IsEnabled { get; init; } = true;
    public bool IsPublic { get; init; }
    public AdRequirement AdRequirement { get; init; }
    public string? Description { get; init; }
}
using VpnHood.Common.Messaging;
using ClientPolicy = VpnHood.AccessServer.Dtos.ClientPolicies.ClientPolicy;

namespace VpnHood.AccessServer.Dtos.AccessTokens;

public class AccessToken
{
    public required Guid ProjectId { get; init; }
    public required Guid AccessTokenId { get; init; }
    public required string? AccessTokenName { get; init; }
    public required int SupportCode { get; init; }
    public required Guid ServerFarmId { get; init; }
    public required string? ServerFarmName { get; init; }
    public required DateTime? FirstUsedTime { get; init; }
    public required DateTime? LastUsedTime { get; init; }
    public required bool IsPublic { get; init; }
    public required ClientPolicy[]? ClientPolicies { get; init; }
    public required long? ClientCode { get; init; }
    public required long? ManagerCode { get; init; }
    public required bool IsEnabled { get; init; }
    public required AdRequirement AdRequirement { get; init; }
    public required DateTime? ExpirationTime { get; init; }
    public required DateTime CreatedTime { get; init; }
    public required DateTime ModifiedTime { get; init; }
    public required string? Description { get; init; }
    
    public required long MaxTraffic { get; init; }
    public required int Lifetime { get; init; }
    public required int MaxDevice { get; init; }
    public required string[] Tags { get; init; }

}
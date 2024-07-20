using VpnHood.AccessServer.Persistence.Enums;

namespace VpnHood.AccessServer.Dtos;

public class Project
{
    public required Guid ProjectId { get; init; }
    public required string? ProjectName { get; init; }
    public required DateTime CreatedTime { get; init; }
    public required SubscriptionType SubscriptionType { get; init; }
    public required string? GaMeasurementId { get; init; }
    public required string? GaApiSecret { get; init; }
    public required string AdRewardSecret { get; init; }
    public required Uri AdRewardUrl { get; init; }
}
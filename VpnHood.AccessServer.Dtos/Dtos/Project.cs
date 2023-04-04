using System;

namespace VpnHood.AccessServer.Dtos;

public class Project
{
    public required Guid ProjectId { get; set; }
    public required string? ProjectName { get; set; }
    public required DateTime CreatedTime { get; init; }
    public required SubscriptionType SubscriptionType { get; set; }
    public required string? GoogleAnalyticsTrackId { get; set; }
}

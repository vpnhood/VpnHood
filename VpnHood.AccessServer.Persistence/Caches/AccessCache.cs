using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.Persistence.Caches;

public class AccessCache : AccessBaseModel
{
    public required int AccessTokenSupportCode { get; init; }
    public required string? AccessTokenName { get; init; }
    public required int MaxDevice { get; init; }
    public required long MaxTraffic { get; init; }
    public required DateTime? ExpirationTime { get; init; }
    public required bool IsPublic { get; init; }
    public required bool IsAccessTokenEnabled { get; init; }

    public AccessModel ToModel()
    {
        return new AccessModel {
            AccessId = AccessId,
            AccessTokenId = AccessTokenId,
            DeviceId = DeviceId,
            CreatedTime = CreatedTime,
            LastUsedTime = LastUsedTime,
            AdRewardExpirationTime = AdRewardExpirationTime,
            AdRewardMinutes = AdRewardMinutes,
            LastCycleSentTraffic = LastCycleSentTraffic,
            LastCycleReceivedTraffic = LastCycleReceivedTraffic,
            LastCycleTraffic = LastCycleTraffic,
            TotalSentTraffic = TotalSentTraffic,
            TotalReceivedTraffic = TotalReceivedTraffic,
            TotalTraffic = TotalTraffic,
            CycleTraffic = CycleTraffic,
            Description = Description
        };
    }
}
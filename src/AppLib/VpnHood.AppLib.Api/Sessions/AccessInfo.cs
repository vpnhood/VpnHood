namespace VpnHood.AppLib.Api.Sessions;

// What the access itself allows and has been used for - the credential's facts, not this session's.
public class AccessInfo
{
    public bool IsNew { get; init; }
    public DateTime CreatedTime { get; init; }
    public DateTime LastUsedTime { get; init; }
    public DateTime? ExpirationTime { get; init; }
    public bool IsPremium { get; init; }
    public long MaxCycleTraffic { get; set; }
    public long MaxTotalTraffic { get; set; }
    public int MaxDeviceCount { get; init; }
    public Traffic? MaxSpeedMbps { get; init; }
    public int DeviceLifeSpan { get; init; }
    public AccessDevicesSummary? DevicesSummary { get; init; }
}

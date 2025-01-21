namespace VpnHood.Core.Common.Messaging;

public class AccessInfo
{
    public bool IsNew { get; init; }
    public DateTime CreatedTime { get; init; }
    public DateTime LastUsedTime { get; init; }
    public DateTime? ExpirationTime { get; init; } // It is the access expiration not session expiration
    public bool IsPremium { get; init; }
    public long MaxCycleTraffic { get; set; }
    public long MaxTotalTraffic { get; set; }
    public int MaxDeviceCount { get; init; }
    public int DeviceLifeSpan { get; init; } // In days
    public int? DeviceCount { get; init; } // The count of devices
    public bool? HasMoreDevices { get; init; } // If true, there are more devices than the count
    public AccessDevice[]? Devices { get; init; } // Recent devices
}
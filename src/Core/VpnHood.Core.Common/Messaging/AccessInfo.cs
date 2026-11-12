namespace VpnHood.Core.Common.Messaging;

/// <summary>
/// Represents an access's terms and state, as its issuer reports them.
/// </summary>
public class AccessInfo
{
    /// <summary>
    /// Gets a value indicating whether the access manager reports the access as new; the file access manager always
    /// reports <see langword="false"/>.
    /// </summary>
    public bool IsNew { get; init; }

    /// <summary>Gets when the access was made, in UTC.</summary>
    public DateTime CreatedTime { get; init; }

    /// <summary>Gets when the access was last used, in UTC.</summary>
    public DateTime LastUsedTime { get; init; }

    /// <summary>Gets when the access expires, in UTC, not a session's, or <see langword="null"/> for never.</summary>
    public DateTime? ExpirationTime { get; init; }

    /// <summary>Gets a value indicating whether the access is premium.</summary>
    public bool IsPremium { get; init; }

    /// <summary>Gets or sets the bytes the access may move in a cycle; 0 where the access manager reports no limit.</summary>
    public long MaxCycleTraffic { get; set; }

    /// <summary>Gets or sets the bytes the access may move in all; 0 where the access manager reports no limit.</summary>
    public long MaxTotalTraffic { get; set; }

    /// <summary>Gets the number of devices that may use the access, 0 for no limit.</summary>
    public int MaxDeviceCount { get; init; }

    /// <summary>Gets the speed limits each way, in megabits a second, or <see langword="null"/>.</summary>
    public Traffic? MaxSpeedMbps { get; init; }

    /// <summary>
    /// Gets, in days, how long the access manager keeps a device's record after its last use, as it reports it.
    /// </summary>
    public int DeviceLifeSpan { get; init; }

    /// <summary>Gets the devices that used the access, or <see langword="null"/>.</summary>
    public AccessDevicesSummary? DevicesSummary { get; init; }
}
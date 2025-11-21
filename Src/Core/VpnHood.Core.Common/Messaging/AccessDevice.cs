namespace VpnHood.Core.Common.Messaging;

// ReSharper disable once ClassNeverInstantiated.Global
public class AccessDevice
{
    public DateTime LastUsedTime { get; set; }
    public string? OperatingSystem { get; set; }
    public string? IpAddress { get; set; }
}
namespace VpnHood.Core.Common.Messaging;

public class AccessDevice
{
    public DateTime LastUsedTime { get; set; }
    public string? OperatingSystem { get; set; }
    public string? IpAddress { get; set; }
}
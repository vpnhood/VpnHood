using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.AccessServer.Dtos.IpLocks;

public class IpLockCreateParams(IPAddress ipAddress)
{
    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress IpAddress { get; set; } = ipAddress;

    public bool IsLocked { get; set; }
    public string? Description { get; set; }
}
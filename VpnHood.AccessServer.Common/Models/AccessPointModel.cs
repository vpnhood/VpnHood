using System.Collections;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.AccessServer.Dtos;
using VpnHood.Common.Converters;

namespace VpnHood.AccessServer.Models;

public class AccessPointModel : IStructuralEquatable, IEquatable<AccessPointModel>
{
    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress IpAddress { get; init; }
    public required AccessPointMode AccessPointMode { get; init; }
    public required bool IsListen { get; init; }
    public required int TcpPort { get; init; }
    public required int UdpPort { get; init; }

    [JsonIgnore]
    public bool IsPublic => AccessPointMode is AccessPointMode.PublicInToken or AccessPointMode.Public;

    public bool Equals(object? other, IEqualityComparer comparer)
    {
        return Equals(this);
    }

    public int GetHashCode(IEqualityComparer comparer)
    {
        return GetHashCode();
    }

    public bool Equals(AccessPointModel? other)
    {
        return Equals((object?)this);
    }
    
    public override int GetHashCode()
    {
        return HashCode.Combine(IpAddress, AccessPointMode, IsListen, TcpPort, UdpPort);
    }

    public override bool Equals(object? other)
    {
        return
            other == this ||
            other is AccessPointModel otherAccessPoint &&
            Equals(IpAddress, otherAccessPoint.IpAddress) &&
            AccessPointMode == otherAccessPoint.AccessPointMode &&
            IsListen == otherAccessPoint.IsListen &&
            TcpPort == otherAccessPoint.TcpPort &&
            UdpPort == otherAccessPoint.UdpPort;
    }

}
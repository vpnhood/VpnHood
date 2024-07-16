using VpnHood.Common.Net;

namespace VpnHood.Client.App;

public class IpGroupRange
{
    public required string IpGroupId { get; init; }
    public required IpRangeOrderedList IpRanges { get; init; }
}
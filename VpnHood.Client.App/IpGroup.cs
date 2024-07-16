using VpnHood.Common.Net;

namespace VpnHood.Client.App;

public class IpGroup
{
    public required string IpGroupId { get; init; }
    public required IpRangeOrderedList IpRanges { get; init; }

    public IpGroupInfo ToInfo()
    {
        return new IpGroupInfo {
            IpGroupId = IpGroupId
        };
    }
}
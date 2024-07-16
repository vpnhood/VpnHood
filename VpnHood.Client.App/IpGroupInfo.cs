using System.Globalization;

namespace VpnHood.Client.App;

public class IpGroupInfo
{
    public required string IpGroupId { get; init; }

    public string IpGroupName {
        get {
            try {
                var regionInfo = new RegionInfo(IpGroupId);
                return regionInfo.EnglishName;
            }
            catch (Exception) {
                return IpGroupId;
            }
        }
    }
}
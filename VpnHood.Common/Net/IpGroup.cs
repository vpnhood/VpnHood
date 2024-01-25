namespace VpnHood.Common.Net;

public class IpGroup(string ipGroupId, string ipGroupName)
{
    public string IpGroupId { get; set; } = ipGroupId;
    public string IpGroupName { get; set; } = ipGroupName;
}
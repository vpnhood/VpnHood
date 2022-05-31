namespace VpnHood.Client.App;

public class IpGroup
{
    public string IpGroupId { get; set; }
    public string IpGroupName { get; set; }

    public IpGroup(string ipGroupId, string ipGroupName)
    {
        IpGroupName = ipGroupName;
        IpGroupId = ipGroupId;
    }
}
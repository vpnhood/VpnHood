namespace VpnHood.Client.App
{
    public class IpGroup
    {
        public IpGroup(string ipGroupName, string ipGroupId)
        {
            IpGroupName = ipGroupName;
            IpGroupId = ipGroupId;
        }

        public string IpGroupName { get; set; }

        public string IpGroupId { get; set; }
    }
}
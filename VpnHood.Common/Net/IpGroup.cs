namespace VpnHood.Common.Net
{
    public class IpGroup
    {
        public string IpGroupId { get; set; }
        public string IpGroupName { get; set; }

        public IpGroup(string ipGroupId, string ipGroupName)
        {
            IpGroupId = ipGroupId;
            IpGroupName = ipGroupName;
        }
    }
}
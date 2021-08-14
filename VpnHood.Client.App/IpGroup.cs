namespace VpnHood.Client
{
    public class IpGroup
    {
        public string IpGroupName { get; set; }
        
        public string IpGroupId { get; set; }

        public IpGroup(string ipGroupName, string ipGroupId)
        {
            IpGroupName = ipGroupName;
            IpGroupId = ipGroupId;
        }
    }
}

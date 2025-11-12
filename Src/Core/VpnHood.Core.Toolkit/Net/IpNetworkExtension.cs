namespace VpnHood.Core.Toolkit.Net;

public static class IpNetworkExtension
{
    extension(IEnumerable<IpNetwork> ipNetworks)
    {
        public IOrderedEnumerable<IpNetwork> Sort()
        {
            return ipNetworks
                .ToIpRanges()
                .ToIpNetworks();
        }

        public IpRangeOrderedList ToIpRanges()
        {
            return ipNetworks
                .Select(x => x.ToIpRange())
                .ToOrderedList();
        }
    }

    extension(IOrderedEnumerable<IpNetwork> ipNetworks)
    {
        public bool IsAll()
        {
            return ipNetworks.SequenceEqual(IpNetwork.All);
        }

        public bool IsAllV4()
        {
            return ipNetworks.SequenceEqual([IpNetwork.AllV4]);
        }

        public bool IsAllV6()
        {
            return ipNetworks.SequenceEqual([IpNetwork.AllV6]);
        }
    }
}
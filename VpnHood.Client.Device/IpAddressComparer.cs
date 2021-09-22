using System.Collections.Generic;
using System.Net;

namespace VpnHood.Client.Device
{
    public class IpAddressComparer : IComparer<IPAddress>
    {
        public int Compare(IPAddress x, IPAddress y)
            => IPAddressUtil.Compare(x, y);
    }
}
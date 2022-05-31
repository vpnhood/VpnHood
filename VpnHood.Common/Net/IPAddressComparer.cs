using System.Collections.Generic;
using System.Net;

namespace VpnHood.Common.Net;

public class IPAddressComparer : IComparer<IPAddress>
{
    public int Compare(IPAddress x, IPAddress y)
        => IPAddressUtil.Compare(x, y);
}
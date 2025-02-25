using System.Net;

namespace VpnHood.Core.Toolkit.Net;

// ReSharper disable once InconsistentNaming
public class IPAddressComparer : IComparer<IPAddress>
{
    public int Compare(IPAddress x, IPAddress y)
        => IPAddressUtil.Compare(x, y);
}
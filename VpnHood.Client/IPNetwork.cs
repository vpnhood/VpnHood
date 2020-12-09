using System.Net;

namespace VpnHood.Client
{
    public class IPNetwork
    {
        public IPNetwork(IPAddress prefix, int prefixLength = 32)
        {
            Prefix = prefix;
            PrefixLength = prefixLength;
        }

        public IPAddress Prefix { get; }
        public int PrefixLength { get; }
    }
}

using System;
using System.Net;

namespace VpnHood.Client.Device
{
    public class IPNetwork
    {
        public IPNetwork(IPAddress prefix, int prefixLength = 32)
        {
            Prefix = prefix;
            PrefixLength = prefixLength;

            // first and last
            var bytes = prefix.GetAddressBytes();
            var mask = (uint)~(0xFFFFFFFFL >> prefixLength);
            Array.Reverse(bytes);

            var t = BitConverter.ToUInt32(bytes, 0);
            var first = BitConverter.GetBytes(t & mask);
            Array.Reverse(first);

            var last = BitConverter.GetBytes((t & mask) | ~mask);
            Array.Reverse(last);

            LastAddress = new IPAddress(last);
            FirstAddress = new IPAddress(first);
        }

        public static IPNetwork Parse(string value)
        {
            try
            {
                var parts = value.Split('/');
                return new IPNetwork(IPAddress.Parse(parts[0]), int.Parse(parts[1]));
            }
            catch
            {
                throw new FormatException($"Could not parse IPNetwork from {value}");
            }
        }

        public override string ToString() => $"{Prefix}/{PrefixLength}";
        
        public IPAddress Prefix { get; }
        public int PrefixLength { get; }
        public IPAddress LastAddress { get; }
        public IPAddress FirstAddress { get; }
    }
}

using System.Net;

namespace VpnHood.Client.Device
{
    public class IpRange
    {
        public long FirstIpAddressLong { get; }
        public long LastIpAddressLong { get; }
        public IPAddress FirstIpAddress => IpAddressFromLong(FirstIpAddressLong);
        public IPAddress LastIpAddress => IpAddressFromLong(LastIpAddressLong);
        public long Total => LastIpAddressLong - FirstIpAddressLong + 1;

        public IpRange(IPAddress firstIpAddress, IPAddress lastIpAddress)
        {
            FirstIpAddressLong = IpAddressToLong(firstIpAddress);
            LastIpAddressLong = IpAddressToLong(lastIpAddress);
        }

        public IpRange(long firstIpAddress, long lastIpAddress)
        {
            FirstIpAddressLong = firstIpAddress;
            LastIpAddressLong = lastIpAddress;
        }

        private static IPAddress IpAddressFromLong(long ipAddress)
            => new((uint)IPAddress.NetworkToHostOrder((int)ipAddress));

        private static long IpAddressToLong(IPAddress ipAddress)
        {
            var bytes = ipAddress.GetAddressBytes();
            return ((long)bytes[0] << 24) | ((long)bytes[1] << 16) | ((long)bytes[2] << 8) | bytes[3];
        }
    }
}

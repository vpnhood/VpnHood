using System;
using System.Collections.Generic;
using System.Net;

namespace VpnHood.Client.Device
{
    public class IPNetwork
    {
        private readonly long _firstIpAddress;
        private readonly long _lastIpAddress;

        public static IPNetwork[] FromIpRange(IPAddress firstIpAddress, IPAddress lastIpAddress)
            => FromIpRange(IpAddressToLong(firstIpAddress), IpAddressToLong(lastIpAddress));

        public static IPNetwork[] FromIpRange(long firstIpAddress, long lastIpAddress)
        {
            var result = new List<IPNetwork>();
            while (lastIpAddress >= firstIpAddress)
            {
                byte maxSize = 32;
                while (maxSize > 0)
                {
                    long mask = IMask(maxSize - 1);
                    long maskBase = firstIpAddress & mask;

                    if (maskBase != firstIpAddress)
                        break;

                    maxSize--;
                }
                double x = Math.Log(lastIpAddress - firstIpAddress + 1) / Math.Log(2);
                byte maxDiff = (byte)(32 - Math.Floor(x));
                if (maxSize < maxDiff)
                {
                    maxSize = maxDiff;
                }
                var ipAddress = IpAddressFromLong(firstIpAddress);
                result.Add(new IPNetwork(ipAddress, maxSize));
                firstIpAddress += (long)Math.Pow(2, 32 - maxSize);
            }
            return result.ToArray();
        }

        private static long IMask(int s)
        {
            return (long)(Math.Pow(2, 32) - Math.Pow(2, 32 - s));
        }

        public static long IpAddressToLong(IPAddress ipAddress)
        {
            var bytes = ipAddress.GetAddressBytes();
            return ((long)bytes[0] << 24) | ((long)bytes[1] << 16) | ((long)bytes[2] << 8) | bytes[3];
        }

        public static IPAddress IpAddressFromLong(long ipAddress)
            => new IPAddress((uint)IPAddress.NetworkToHostOrder((int)ipAddress));

        public IPNetwork(IPAddress prefix, int prefixLength = 32)
        {
            if (prefix.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                throw new NotSupportedException("IPv6 is not supported");

            Prefix = prefix;
            PrefixLength = prefixLength;

            var mask = (uint)~(0xFFFFFFFFL >> prefixLength);
            _firstIpAddress = IpAddressToLong(Prefix) & mask;
            _lastIpAddress = _firstIpAddress | ~mask;
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
        public IPAddress LastAddress => IpAddressFromLong(_lastIpAddress);
        public IPAddress FirstAddress => IpAddressFromLong(_firstIpAddress);
        public long Total => _lastIpAddress - _firstIpAddress + 1;
    }
}

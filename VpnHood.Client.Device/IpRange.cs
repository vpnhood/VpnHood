using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json.Serialization;

namespace VpnHood.Client.Device
{
    [JsonConverter(typeof(IpRangeConverter))]
    public class IpRange
    {
        public IpRange(IPAddress ipAddress) : this(ipAddress, ipAddress)
        {
        }

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

        public long FirstIpAddressLong { get; }
        public long LastIpAddressLong { get; }
        public IPAddress FirstIpAddress => IpAddressFromLong(FirstIpAddressLong);
        public IPAddress LastIpAddress => IpAddressFromLong(LastIpAddressLong);
        public long Total => LastIpAddressLong - FirstIpAddressLong + 1;

        public static IpRange[] Sort(IEnumerable<IpRange> ipRanges)
        {
            return Unify(ipRanges.OrderBy(x => x.FirstIpAddressLong));
        }

        private static IpRange[] Unify(IEnumerable<IpRange> sortedIpRanges)
        {
            List<IpRange> res = new();
            foreach (var ipRange in sortedIpRanges)
                if (res.Count > 0 && ipRange.FirstIpAddressLong <= res[^1].LastIpAddressLong)
                {
                    if (ipRange.LastIpAddressLong > res[^1].LastIpAddressLong)
                        res[^1] = new IpRange(res[^1].FirstIpAddress, ipRange.LastIpAddress);
                }
                else
                {
                    res.Add(ipRange);
                }

            return res.ToArray();
        }


        public static IpRange[] Invert(IEnumerable<IpRange> ipRanges)
        {
            // sort
            var ipRangesSorted = Sort(ipRanges);

            // invert of nothing is all thing!
            if (ipRangesSorted.Length == 0)
                return new[] {Parse("0.0.0.0-255.255.255.255")};

            // extract
            List<IpRange> res = new();
            for (var i = 0; i < ipRangesSorted.Length; i++)
            {
                var ipRange = ipRangesSorted[i];
                if (i == 0 && ipRange.FirstIpAddressLong != 0) res.Add(new IpRange(0, ipRange.FirstIpAddressLong - 1));
                if (i > 0)
                    res.Add(new IpRange(ipRangesSorted[i - 1].LastIpAddressLong + 1, ipRange.FirstIpAddressLong - 1));
                if (i == ipRangesSorted.Length - 1 && ipRange.LastIpAddressLong != 0xFFFFFFFF)
                    res.Add(new IpRange(ipRange.LastIpAddressLong + 1, 0xFFFFFFFF));
            }

            return res.ToArray();
        }

        public static IpRange Parse(string value)
        {
            var items = value.Replace("to", "-").Split('-');
            if (items.Length == 1)
                return new IpRange(IPAddress.Parse(items[0].Trim()));
            if (items.Length == 2)
                return new IpRange(IPAddress.Parse(items[0].Trim()), IPAddress.Parse(items[1].Trim()));
            throw new FormatException($"Could not parse {nameof(IpRange)} from: {value}!");
        }

        public override string ToString()
        {
            return $"{FirstIpAddress}-{LastIpAddress}";
        }

        public override bool Equals(object obj)
        {
            return obj is IpRange ipRange &&
                   FirstIpAddress.Equals(ipRange.FirstIpAddress) &&
                   LastIpAddress.Equals(ipRange.LastIpAddress);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(FirstIpAddress, LastIpAddress);
        }

        private static IPAddress IpAddressFromLong(long ipAddress)
        {
            return new IPAddress((uint) IPAddress.NetworkToHostOrder((int) ipAddress));
        }

        private static long IpAddressToLong(IPAddress ipAddress)
        {
            var bytes = ipAddress.GetAddressBytes();
            return ((long) bytes[0] << 24) | ((long) bytes[1] << 16) | ((long) bytes[2] << 8) | bytes[3];
        }

        public bool IsInRange(IPAddress ipAddress)
        {
            var ipAddressLong = IpAddressToLong(ipAddress);
            return ipAddressLong < FirstIpAddressLong || ipAddressLong > LastIpAddressLong;
        }

        public static int CompareIpAddress(IPAddress ipAddress1, IPAddress ipAddress2)
        {
            return (int) (IpAddressToLong(ipAddress1) - IpAddressToLong(ipAddress2));
        }

        /// <summary>
        ///     Search in ipRanges using binary search
        /// </summary>
        /// <param name="sortedIpRanges">a sorted ipRanges</param>
        /// <param name="ipAddress">search value</param>
        /// <returns></returns>
        public static bool IsInRangeFast(IpRange[] sortedIpRanges, IPAddress ipAddress)
        {
            var res = Array.BinarySearch(sortedIpRanges, new IpRange(ipAddress, ipAddress),
                new IpRangeSearchComparer());
            return res >= 0 && res < sortedIpRanges.Length;
        }

        private class IpRangeSearchComparer : IComparer<IpRange>
        {
            public int Compare(IpRange x, IpRange y)
            {
                if (x.FirstIpAddressLong <= y.FirstIpAddressLong &&
                    x.LastIpAddressLong >= y.LastIpAddressLong) return 0;
                if (x.FirstIpAddressLong < y.FirstIpAddressLong) return -1;
                return +1;
            }
        }
    }
}
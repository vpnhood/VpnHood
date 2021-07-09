using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace VpnHood.Client.Device
{
    public class IpRange
    {
        private class IpRangeSearchComparer : IComparer<IpRange>
        {
            public int Compare(IpRange x, IpRange y)
            {
                if (x.FirstIpAddressLong < y.FirstIpAddressLong) return -1;
                else if (x.LastIpAddressLong > y.LastIpAddressLong) return +1;
                else return 0;
            }
        }

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

        public static IpRange[] Unify(IEnumerable<IpRange> ipRanges)
        {
            List<IpRange> res = new();
            var ipSortedRanges = Sort(ipRanges);
            foreach (var ipRange in ipSortedRanges)
            {
                if (res.Count > 0 && ipRange.FirstIpAddressLong <= res[^1].LastIpAddressLong)
                {
                    if (ipRange.LastIpAddressLong > res[^1].LastIpAddressLong)
                        res[^1] = new(res[^1].FirstIpAddress, ipRange.LastIpAddress);
                    continue;
                }
                else
                {
                    res.Add(ipRange);
                }
            }

            return res.ToArray();
        }


        public static IpRange[] Invert(IpRange[] ipRanges)
        {
            // invert of nothing is all thing!
            if (ipRanges.Length == 0)
                return new[] { Parse("0.0.0.0-255.255.255.255") }; 

            // sort
            var ipRangesU = Unify(ipRanges);

            // extract
            List<IpRange> res = new();
            for (var i = 0; i < ipRangesU.Length; i++)
            {
                var ipRange = ipRangesU[i];
                if (i == 0 && ipRange.FirstIpAddressLong != 0) res.Add(new IpRange(0, ipRange.FirstIpAddressLong - 1));
                if (i > 0) res.Add(new IpRange(ipRangesU[i - 1].LastIpAddressLong + 1, ipRange.FirstIpAddressLong - 1));
                if (i == ipRangesU.Length - 1 && ipRange.LastIpAddressLong != 0xFFFFFFFF) res.Add(new IpRange(ipRange.LastIpAddressLong + 1, 0xFFFFFFFF));
            }

            return res.ToArray();
        }

        public static IpRange Parse(string value)
        {
            var items = value.Replace("to", "-").Split('-');
            if (items.Length != 2) throw new FormatException($"Invalid {nameof(IpRange)} format!");
            return new IpRange(IPAddress.Parse(items[0].Trim()), IPAddress.Parse(items[1].Trim()));
        }

        public override string ToString() => $"{FirstIpAddress}-{LastIpAddress}";

        public override bool Equals(object obj)
            => obj is IpRange ipRange && 
            FirstIpAddress.Equals(ipRange.FirstIpAddress) && 
            LastIpAddress.Equals(ipRange.LastIpAddress);

        public override int GetHashCode()
            => HashCode.Combine(FirstIpAddress, LastIpAddress);

        private static IPAddress IpAddressFromLong(long ipAddress)
            => new((uint)IPAddress.NetworkToHostOrder((int)ipAddress));

        private static long IpAddressToLong(IPAddress ipAddress)
        {
            var bytes = ipAddress.GetAddressBytes();
            return ((long)bytes[0] << 24) | ((long)bytes[1] << 16) | ((long)bytes[2] << 8) | bytes[3];
        }

        public bool IsInRange(IPAddress ipAddress)
        {
            var ipAddressLong = IpAddressToLong(ipAddress);
            return (ipAddressLong < FirstIpAddressLong) || (ipAddressLong > LastIpAddressLong);
        }

        public static int CompareIpAddress(IPAddress ipAddress1, IPAddress ipAddress2)
            => (int)(IpAddressToLong(ipAddress1) - IpAddressToLong(ipAddress2));

        public static IOrderedEnumerable<IpRange> Sort(IEnumerable<IpRange> ipRanges)
            => ipRanges.OrderBy(x => x.FirstIpAddressLong);

        /// <summary>
        /// Search in ipRanges using binarysearch
        /// </summary>
        /// <param name="sortedIpRanges">a sorted ipRanges</param>
        /// <param name="ipAddress">search value</param>
        /// <returns></returns>
        public static bool IsInRange(IpRange[] sortedIpRanges, IPAddress ipAddress)
        {
            var res = Array.BinarySearch(sortedIpRanges, new IpRange(ipAddress, ipAddress), new IpRangeSearchComparer());
            return res > 0 && res < sortedIpRanges.Length;
        }
    }
}

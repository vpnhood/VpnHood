using System.Buffers.Binary;
using System.Net;
using System.Numerics;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Test.Tests;

[TestClass]
public class IpAddressValueTest : TestBase
{
    private static readonly string[] AddressTexts = [
        "0.0.0.0", "10.0.0.1", "10.0.0.2", "8.8.8.8", "192.168.1.254", "255.255.255.255",
        "::", "::1", "::10.0.0.2", "::ffff:10.0.0.2", "::ffff:255.255.255.255", "2001:db8::1",
        "2001:db8::1:0:0:1", "fe80::1", "fe80::1%3", "fe80::1%4294967295", "ff02::1", "1::",
        "ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff"
    ];

    [TestMethod]
    public void Converts_both_ways_as_IPAddress_does()
    {
        foreach (var ipAddress in AddressTexts.Select(IPAddress.Parse)) {
            var value = ipAddress.ToValue();
            var back = value.ToIPAddress();
            Assert.AreEqual(ipAddress, back, $"round trip of {ipAddress}");
            Assert.AreEqual(ipAddress.IsV6 ? ipAddress.ScopeId : 0, back.IsV6 ? back.ScopeId : 0, $"scope of {ipAddress}");
            Assert.AreEqual(ipAddress.ToString(), value.ToString());
            Assert.AreEqual(ipAddress.IsV4, value.IsV4, $"IsV4 of {ipAddress}");
            Assert.AreEqual(ipAddress.IsV6, value.IsV6, $"IsV6 of {ipAddress}");
            Assert.AreEqual(ipAddress.IsIPv4MappedToIPv6, value.IsIPv4MappedToIPv6, $"mapped {ipAddress}");
            Assert.AreEqual(ipAddress.MapToIPv4(), value.MapToIPv4().ToIPAddress(), $"MapToIPv4 of {ipAddress}");

            // the bytes as a header holds them, both ways
            var bytes = ipAddress.GetAddressBytes();
            var written = new byte[20];
            Assert.IsTrue(value.TryWriteBytes(written, out var count), $"bytes of {ipAddress}");
            CollectionAssert.AreEqual(bytes, written[..count], $"bytes of {ipAddress}");
            Assert.IsFalse(value.TryWriteBytes(new byte[bytes.Length - 1], out var none), $"short span {ipAddress}");
            Assert.AreEqual(0, none);
            Assert.AreEqual(new IPAddress(bytes), new IpAddressValue(bytes).ToIPAddress(), $"span of {ipAddress}");
        }
    }

    [TestMethod]
    public void Equals_as_IPAddress_does()
    {
        var addresses = AddressTexts.Select(IPAddress.Parse).ToArray();
        foreach (var a in addresses)
        foreach (var b in addresses) {
            var equal = a.ToValue() == b.ToValue();
            Assert.AreEqual(a.Equals(b), equal, $"{a} == {b}");
            Assert.AreEqual(!equal, a.ToValue() != b.ToValue(), $"{a} != {b}");
            Assert.AreEqual(equal, a.ToValue().Equals((object)b.ToValue()), $"{a} Equals(object) {b}");
            if (equal)
                Assert.AreEqual(a.ToValue().GetHashCode(), b.ToValue().GetHashCode(), $"hash of {a} and {b}");
        }
    }

    [TestMethod]
    public void Hash_takes_each_word_on_its_own()
    {
        // a ulong's own hash folds its two halves before any seed: these two collided in every run
        Assert.AreNotEqual(
            IPAddress.Parse("2001:db8::1").ToValue().GetHashCode(),
            IPAddress.Parse("2001:db8::1:0:0").ToValue().GetHashCode());

        // 65536 addresses whose low words all fold to one number, (x << 32) | (x ^ c), hash apart
        var hashes = new HashSet<int>();
        var bytes = IPAddress.Parse("2001:db8::").GetAddressBytes();
        for (uint x = 0; x < 65536; x++) {
            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8), ((ulong)x << 32) | (x ^ 0x12345678u));
            hashes.Add(new IpAddressValue(bytes).GetHashCode());
        }

        Assert.IsTrue(hashes.Count > 65000, $"{hashes.Count} distinct hashes");
    }

    [TestMethod]
    public void Default_holds_no_address()
    {
        var empty = default(IpAddressValue);
        Assert.IsFalse(empty.IsV4);
        Assert.IsFalse(empty.IsV6);
        Assert.IsFalse(empty.IsIPv4MappedToIPv6);
        Assert.AreEqual(string.Empty, empty.ToString());
        Assert.IsTrue(empty == default);
        Assert.IsTrue(empty != IPAddress.Any.ToValue());
        Assert.IsTrue(empty != IPAddress.IPv6Any.ToValue());
        Assert.IsTrue(empty < IPAddress.Any.ToValue());
        Assert.ThrowsExactly<InvalidOperationException>(() => empty.ToIPAddress());
        Assert.ThrowsExactly<InvalidOperationException>(() => empty.MapToIPv4());
        Assert.ThrowsExactly<InvalidOperationException>(() => empty.TryWriteBytes(new byte[16], out _));
    }

    [TestMethod]
    public void Takes_4_or_16_bytes()
    {
        foreach (var length in new[] { 0, 3, 5, 15, 17 }) {
            var ex = Assert.ThrowsExactly<ArgumentException>(() => new IpAddressValue(new byte[length]));
            Assert.AreEqual("address", ex.ParamName);
        }
    }

    [TestMethod]
    public void Orders_agree_with_Equals_and_IPAddressUtil_Compare()
    {
        CheckOrders(AddressTexts.Select(x => (IPAddress?)IPAddress.Parse(x)).Append(null).ToArray());

        // random sets with near and equal numbers across the forms, and the default value among them
        var random = new Random(42);
        var pool = Enumerable.Range(0, 400).Select(_ => RandomAddress(random)).ToArray();
        for (var i = 0; i < 40; i++)
            CheckOrders(Enumerable.Range(0, 40)
                .Select(_ => random.Next(10) == 0 ? null : (IPAddress?)pool[random.Next(pool.Length)])
                .ToArray());
    }

    // Both orders on every pair and triple; null stands for the default value.
    private static void CheckOrders(IPAddress?[] addresses)
    {
        var values = addresses.Select(x => x == null ? default : x.ToValue()).ToArray();
        for (var i = 0; i < addresses.Length; i++)
        for (var j = 0; j < addresses.Length; j++) {
            IPAddress? a = addresses[i], b = addresses[j];
            IpAddressValue va = values[i], vb = values[j];

            // IPAddressUtil.Compare against an order built apart from it, and its value overload against it
            var compare = Math.Sign(IPAddressUtil.Compare(a, b));
            if (compare != Math.Sign(ReferenceCompare(a, b)))
                Assert.Fail($"IPAddressUtil.Compare({a}, {b}) is {compare}");
            if (Math.Sign(IPAddressUtil.Compare(va, vb)) != compare)
                Assert.Fail($"IPAddressUtil.Compare of the values {a} and {b}");

            // CompareTo: zero only for equal values, antisymmetric, IPv4 before IPv6, the default first, the operators
            // with it, and as IPAddressUtil.Compare where no mapped address or scope id makes them differ
            var order = Math.Sign(va.CompareTo(vb));
            if ((order == 0) != va.Equals(vb)) Assert.Fail($"CompareTo({a}, {b}) is {order}, Equals is {va.Equals(vb)}");
            if (order != -Math.Sign(vb.CompareTo(va))) Assert.Fail($"CompareTo of {a} and {b} not antisymmetric");
            if ((va < vb) != (order < 0) || (va > vb) != (order > 0) ||
                (va <= vb) != (order <= 0) || (va >= vb) != (order >= 0))
                Assert.Fail($"operators on {a} and {b}");
            if (va.IsV4 && vb.IsV6 && order >= 0) Assert.Fail($"{a} not before {b}");
            if (a == null && b != null && order >= 0) Assert.Fail($"the default not before {b}");
            if (a != null && b != null && !a.IsIPv4MappedToIPv6 && !b.IsIPv4MappedToIPv6 &&
                ScopeOf(a) == ScopeOf(b) && order != compare)
                Assert.Fail($"CompareTo({a}, {b}) is {order}, IPAddressUtil.Compare is {compare}");
        }

        foreach (var a in values)
        foreach (var b in values)
        foreach (var c in values) {
            if (a.CompareTo(b) <= 0 && b.CompareTo(c) <= 0 && a.CompareTo(c) > 0)
                Assert.Fail($"CompareTo not transitive on {a}, {b}, {c}");
            if (IPAddressUtil.Compare(a, b) <= 0 && IPAddressUtil.Compare(b, c) <= 0 && IPAddressUtil.Compare(a, c) > 0)
                Assert.Fail($"IPAddressUtil.Compare not transitive on {a}, {b}, {c}");
        }
    }

    // The order IPAddressUtil.Compare should give: null first; an IPv4-mapped address as its IPv4 address; IPv4
    // before IPv6; then the number; scope ids left out.
    private static int ReferenceCompare(IPAddress? a, IPAddress? b)
    {
        if (a == null || b == null)
            return (a == null ? 0 : 1) - (b == null ? 0 : 1);

        var bytesA = ReferenceBytes(a);
        var bytesB = ReferenceBytes(b);
        return bytesA.Length != bytesB.Length
            ? bytesA.Length - bytesB.Length
            : new BigInteger(bytesA, isUnsigned: true, isBigEndian: true)
                .CompareTo(new BigInteger(bytesB, isUnsigned: true, isBigEndian: true));
    }

    // The address's bytes; an IPv4-mapped address's are its IPv4 address's.
    private static byte[] ReferenceBytes(IPAddress ipAddress)
    {
        var bytes = ipAddress.GetAddressBytes();
        var isMapped = bytes.Length == 16 && bytes.Take(10).All(x => x == 0) && bytes[10] == 0xFF && bytes[11] == 0xFF;
        return isMapped ? bytes[12..] : bytes;
    }

    private static long ScopeOf(IPAddress ipAddress) => ipAddress.IsV6 ? ipAddress.ScopeId : 0;

    // IPv4, IPv4-mapped, IPv4-compatible (::a.b.c.d), link-local IPv6 with a few scope ids, and IPv6 anywhere.
    private static IPAddress RandomAddress(Random random)
    {
        var v4 = new byte[4];
        random.NextBytes(v4);
        if (random.Next(2) == 0) v4[0] = 10; // many in one network
        var v6 = new byte[16];
        switch (random.Next(5)) {
            case 0:
                return new IPAddress(v4);

            case 1:
                v6[10] = 0xFF;
                v6[11] = 0xFF;
                v4.CopyTo(v6, 12);
                return new IPAddress(v6);

            case 2:
                v4.CopyTo(v6, 12);
                return new IPAddress(v6);

            case 3:
                v6[0] = 0xFE;
                v6[1] = 0x80;
                v6[15] = (byte)random.Next(4);
                return new IPAddress(v6, random.Next(3));

            default:
                random.NextBytes(v6);
                return new IPAddress(v6);
        }
    }
}

using System.Buffers;
using System.Net;
using VpnHood.Core.Tunneling;
using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

// Who owns a packet's memory, and that the packet gives it back once
[TestClass]
public class PacketMemoryTest : TestBase
{
    // as an adapter's read rents for its MTU: a packet at the start of a longer array
    private const int RentLength = 1500;

    private static IpPacket BuildUdp(IpVersion ipVersion)
    {
        var address = ipVersion == IpVersion.IPv4 ? IPAddress.Parse("10.0.0.2") : IPAddress.Parse("fd00::2");
        return PacketBuilder.BuildUdp(new IPEndPoint(address, 5353), new IPEndPoint(address, 53), "hello"u8);
    }

    // a packet near an MTU, which a read rents a 2 KB array for
    private static byte[] BuildPacketBytes(int packetLength)
    {
        var address = IPAddress.Parse("10.0.0.2");
        using var ipPacket = PacketBuilder.BuildUdp(new IPEndPoint(address, 5353), new IPEndPoint(address, 53),
            new byte[packetLength - 28]);
        return ipPacket.Buffer.ToArray();
    }

    // 100 calls to warm up, then the average of 1000
    private static double AllocatedBytesPerCall(Action action)
    {
        for (var i = 0; i < 100; i++)
            action();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            action();

        return (GC.GetAllocatedBytesForCurrentThread() - before) / 1000.0;
    }

    [TestMethod]
    [DataRow(IpVersion.IPv4)]
    [DataRow(IpVersion.IPv6)]
    public void A_packet_on_a_rented_array_returns_it_to_its_pool_once(IpVersion ipVersion)
    {
        using var source = BuildUdp(ipVersion);
        var arrayPool = new TestArrayPool();
        var rentedArray = arrayPool.Rent(RentLength);
        source.Buffer.Span.CopyTo(rentedArray);

        var ipPacket = PacketBuilder.Attach(PacketMemory.FromRentedArray(rentedArray, source.PacketLength, arrayPool));
        Assert.AreEqual(source.PacketLength, ipPacket.PacketLength);
        CollectionAssert.AreEqual(source.Buffer.ToArray(), ipPacket.Buffer.ToArray());

        ipPacket.Dispose();
        ipPacket.Dispose();
        Assert.HasCount(1, arrayPool.Returned);
        Assert.AreSame(rentedArray, arrayPool.Returned[0]);
        Assert.ThrowsExactly<ObjectDisposedException>(() => ipPacket.Buffer);
    }

    [TestMethod]
    public void A_rented_array_goes_back_to_the_shared_pool_unless_a_pool_is_given()
    {
        var bytes = BuildPacketBytes(1400);

        // never returned, each packet would cost a new array
        Assert.IsLessThan(RentLength, AllocatedBytesPerCall(() => {
            var rentedArray = ArrayPool<byte>.Shared.Rent(RentLength);
            bytes.CopyTo(rentedArray, 0);
            PacketBuilder.Attach(PacketMemory.FromRentedArray(rentedArray, bytes.Length)).Dispose();
        }));
    }

    [TestMethod]
    public void Memory_an_array_or_a_segment_stays_the_callers()
    {
        using var source = BuildUdp(IpVersion.IPv4);
        var bytes = new byte[64]; // a length the shared pool takes
        source.Buffer.Span.CopyTo(bytes);

        PacketBuilder.Attach(bytes).Dispose();
        PacketBuilder.Attach(bytes.AsMemory()).Dispose();
        PacketBuilder.Attach(new ArraySegment<byte>(bytes)).Dispose();

        // returned, the pool would lend it to the next renter while the caller still has it
        var nextRenter = ArrayPool<byte>.Shared.Rent(bytes.Length);
        Assert.AreNotSame(bytes, nextRenter);
        ArrayPool<byte>.Shared.Return(nextRenter);
    }

    [TestMethod]
    public void A_rented_array_whose_packet_fails_to_parse_stays_the_callers()
    {
        var arrayPool = new TestArrayPool();
        var rentedArray = arrayPool.Rent(64);
        rentedArray[0] = 0x45; // IPv4 with a 20-byte header
        rentedArray[2] = 0xFF; // and a total length past the array
        rentedArray[3] = 0xFF;

        Assert.ThrowsExactly<ArgumentException>(() =>
            PacketBuilder.Attach(PacketMemory.FromRentedArray(rentedArray, rentedArray.Length, arrayPool)));
        Assert.IsEmpty(arrayPool.Returned);
    }

    [TestMethod]
    public void A_header_that_claims_more_than_the_rented_length_is_refused()
    {
        // the header says 1400 bytes, and only the first 60 hold this packet: the rest are the array's from before
        var arrayPool = new TestArrayPool();
        var rentedArray = arrayPool.Rent(RentLength);
        BuildPacketBytes(1400).CopyTo(rentedArray, 0);

        Assert.ThrowsExactly<ArgumentException>(() =>
            PacketBuilder.Attach(PacketMemory.FromRentedArray(rentedArray, 60, arrayPool)));
    }

    [TestMethod]
    public void A_rented_array_whose_packet_fails_to_build_stays_the_callers()
    {
        var arrayPool = new TestArrayPool();
        var rentedArray = arrayPool.Rent(64);

        // 10 bytes: shorter than an IPv4 header
        var memory = PacketMemory.FromRentedArray(rentedArray, rentedArray.Length, arrayPool);
        Assert.ThrowsExactly<ArgumentException>(() => new IpV4Packet(memory, 10, IpProtocol.Udp, 0));
        Assert.IsEmpty(arrayPool.Returned);
    }

    [TestMethod]
    public void Parse_returns_the_memory_it_rented_when_the_packet_fails_to_parse()
    {
        var bytes = new byte[20];
        bytes[0] = 0x44; // IPv4 with a header length below the minimum
        bytes[3] = 20; // in a 20-byte packet
        var memoryPool = new TestMemoryPool();

        Assert.ThrowsExactly<ArgumentException>(() => PacketBuilder.Parse(bytes, memoryPool));
        Assert.AreEqual(1, memoryPool.Rented.Single().DisposeCount);
    }

    [TestMethod]
    public void BuildIp_returns_the_memory_it_rented_when_the_length_is_refused()
    {
        var address = IPAddress.Parse("10.0.0.2");
        var memoryPool = new TestMemoryPool();

        // 20 + 65516 bytes: past what an IPv4 total length holds
        Assert.ThrowsExactly<ArgumentException>(() =>
            PacketBuilder.BuildIp(address, address, IpProtocol.Udp, 65516, memoryPool));
        Assert.AreEqual(1, memoryPool.Rented.Single().DisposeCount);
    }

    [TestMethod]
    public void BuildTcp_returns_the_memory_it_rented_when_the_options_are_refused()
    {
        var endPoint = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 443);
        var payload = new byte[60_000]; // in a 64 KB array, which shows in the bytes per call if never returned
        var options = new byte[3]; // not a multiple of 4

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PacketBuilder.BuildTcp(endPoint, endPoint, options, payload));
        Assert.IsLessThan(payload.Length, AllocatedBytesPerCall(() => {
            try {
                PacketBuilder.BuildTcp(endPoint, endPoint, options, payload);
            }
            catch (ArgumentOutOfRangeException) {
                // refused, as checked above
            }
        }));
    }

    [TestMethod]
    [DataRow(IpVersion.IPv4)]
    [DataRow(IpVersion.IPv6)]
    public void A_packet_with_a_memory_owner_disposes_it_once(IpVersion ipVersion)
    {
        using var source = BuildUdp(ipVersion);
        var memoryOwner = new TestMemoryOwner(source.Buffer.ToArray());

        var ipPacket = PacketBuilder.Attach(new PacketMemory(memoryOwner));
        ipPacket.Dispose();
        ipPacket.Dispose();
        Assert.AreEqual(1, memoryOwner.DisposeCount);
    }

    [TestMethod]
    public void A_packet_whose_owner_fails_to_dispose_is_disposed_all_the_same()
    {
        using var source = BuildUdp(IpVersion.IPv4);
        var memoryOwner = new TestMemoryOwner(source.Buffer.ToArray()) { FailsFirstDispose = true };
        var ipPacket = PacketBuilder.Attach(new PacketMemory(memoryOwner));

        // the fault shows, and a second Dispose does not give the memory back again
        Assert.ThrowsExactly<InvalidOperationException>(ipPacket.Dispose);
        Assert.ThrowsExactly<ObjectDisposedException>(() => ipPacket.Buffer);
        ipPacket.Dispose();
        Assert.AreEqual(1, memoryOwner.DisposeCount);
    }

    [TestMethod]
    public void A_packet_whose_pool_fails_to_take_its_array_is_disposed_all_the_same()
    {
        using var source = BuildUdp(IpVersion.IPv4);
        var arrayPool = new TestArrayPool { FailsFirstReturn = true };
        var rentedArray = arrayPool.Rent(RentLength);
        source.Buffer.Span.CopyTo(rentedArray);
        var ipPacket = PacketBuilder.Attach(PacketMemory.FromRentedArray(rentedArray, source.PacketLength, arrayPool));

        // the fault shows, and a second Dispose does not return the array again
        Assert.ThrowsExactly<InvalidOperationException>(ipPacket.Dispose);
        Assert.ThrowsExactly<ObjectDisposedException>(() => ipPacket.Buffer);
        ipPacket.Dispose();
        Assert.HasCount(1, arrayPool.Returned);
    }

    [TestMethod]
    public void A_memory_owner_that_is_also_an_array_pool_is_refused()
    {
        using var poolThatOwnsMemory = new PoolThatOwnsMemory();
        Assert.ThrowsExactly<ArgumentException>(() => new PacketMemory(poolThatOwnsMemory));
    }

    [TestMethod]
    public void A_packet_read_from_a_stream_returns_its_array_to_the_pool()
    {
        var bytes = BuildPacketBytes(1400);
        using var stream = new MemoryStream();
        for (var i = 0; i < 1100; i++) // one packet for each of AllocatedBytesPerCall's calls
            stream.Write(bytes);

        stream.Position = 0;
        using var reader = new StreamPacketReader(stream, bufferSize: 4096);

        // never returned, each packet would cost a new array
        Assert.IsLessThan(RentLength, AllocatedBytesPerCall(() => {
            using var ipPacket = reader.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        }));
    }

    [TestMethod]
    public void A_packet_read_from_a_tun_adapter_returns_its_array_to_the_pool()
    {
        var bytes = BuildPacketBytes(1400);
        using var adapter = new OnePacketVpnAdapter(bytes, bytes.Length);

        // never returned, each packet would cost a new array
        Assert.IsLessThan(RentLength, AllocatedBytesPerCall(() => adapter.ReadOnePacket(RentLength)?.Dispose()));
    }

    [TestMethod]
    public void A_tun_read_whose_header_claims_more_than_was_read_is_refused()
    {
        // a 60-byte read whose header says 1400: the other 1340 bytes are what the array held before
        using var adapter = new OnePacketVpnAdapter(BuildPacketBytes(1400), readLength: 60);

        Assert.ThrowsExactly<ArgumentException>(() => adapter.ReadOnePacket(RentLength));
    }

    [TestMethod]
    public void A_packet_parsed_or_built_without_a_pool_makes_no_owner_object()
    {
        using var source = BuildUdp(IpVersion.IPv4);
        var bytes = source.Buffer.ToArray();
        var address = IPAddress.Parse("10.0.0.2");

        Assert.IsLessThan(
            AllocatedBytesPerCall(() => PacketBuilder.Parse(bytes, MemoryPool<byte>.Shared).Dispose()),
            AllocatedBytesPerCall(() => PacketBuilder.Parse(bytes).Dispose()));

        Assert.IsLessThan(
            AllocatedBytesPerCall(() =>
                PacketBuilder.BuildIp(address, address, IpProtocol.Udp, 100, MemoryPool<byte>.Shared).Dispose()),
            AllocatedBytesPerCall(() => PacketBuilder.BuildIp(address, address, IpProtocol.Udp, 100).Dispose()));
    }

    // both a pool and a memory owner: a PacketMemory could not tell which of the two to give the memory back to
    private sealed class PoolThatOwnsMemory : ArrayPool<byte>, IMemoryOwner<byte>
    {
        public Memory<byte> Memory => new byte[64];

        public override byte[] Rent(int minimumLength) => new byte[minimumLength];

        public override void Return(byte[] array, bool clearArray = false)
        {
        }

        public void Dispose()
        {
        }
    }

    // a TUN device whose every read gives the first readLength of the same bytes, over the rest from before
    private sealed class OnePacketVpnAdapter(byte[] bytes, int readLength)
        : NullVpnAdapter(autoDisposePackets: true, blocking: false)
    {
        public IpPacket? ReadOnePacket(int mtu)
        {
            return ReadPacket(mtu);
        }

        protected override int ReadPacket(byte[] buffer)
        {
            bytes.CopyTo(buffer, 0);
            return readLength;
        }
    }
}

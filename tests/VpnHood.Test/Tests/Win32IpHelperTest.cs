using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using VpnHood.Net.VpnAdapters.WinTun.WinNative;

namespace VpnHood.Test.Tests;

// The IP Helper rows go to Windows as they are, so each field must sit where the Windows SDK's netioapi.h
// puts it: offsetof on x64 and x86 gives these, and arm64 lays them out as x64, as they hold no pointers.
[TestClass]
public class Win32IpHelperTest
{
    private static readonly (string Field, int Offset)[] InterfaceRowOffsets = [
        ("Family", 0), ("InterfaceLuid", 8), ("InterfaceIndex", 16), ("MaxReassemblySize", 20),
        ("InterfaceIdentifier", 24), ("MinRouterAdvertisementInterval", 32), ("MaxRouterAdvertisementInterval", 36),
        ("AdvertisingEnabled", 40), ("ForwardingEnabled", 41), ("WeakHostSend", 42), ("WeakHostReceive", 43),
        ("UseAutomaticMetric", 44), ("UseNeighborUnreachabilityDetection", 45),
        ("ManagedAddressConfigurationSupported", 46), ("OtherStatefulConfigurationSupported", 47),
        ("AdvertiseDefaultRoute", 48), ("RouterDiscoveryBehavior", 52), ("DadTransmits", 56),
        ("BaseReachableTime", 60), ("RetransmitTime", 64), ("PathMtuDiscoveryTimeout", 68),
        ("LinkLocalAddressBehavior", 72), ("LinkLocalAddressTimeout", 76), ("ZoneIndices", 80),
        ("SitePrefixLength", 144), ("Metric", 148), ("NlMtu", 152), ("Connected", 156),
        ("SupportsWakeUpPatterns", 157), ("SupportsNeighborDiscovery", 158), ("SupportsRouterDiscovery", 159),
        ("ReachableTime", 160), ("TransmitOffload", 164), ("ReceiveOffload", 165), ("DisableDefaultRoutes", 166)
    ];

    private static readonly (string Field, int Offset)[] UnicastAddressRowOffsets = [
        ("Address", 0), ("InterfaceLuid", 32), ("InterfaceIndex", 40), ("PrefixOrigin", 44), ("SuffixOrigin", 48),
        ("ValidLifetime", 52), ("PreferredLifetime", 56), ("OnLinkPrefixLength", 60), ("SkipAsSource", 61),
        ("DadState", 64), ("ScopeId", 68), ("CreationTimeStamp", 72)
    ];

    [TestMethod]
    public void Rows_keep_the_windows_sdk_layout()
    {
        AssertLayout<Win32IpHelper.MIB_IPINTERFACE_ROW>(168, InterfaceRowOffsets);
        AssertLayout<Win32IpHelper.MIB_UNICASTIPADDRESS_ROW>(80, UnicastAddressRowOffsets);
    }

    [TestMethod]
    public void Interface_row_reads_the_loopback_interface()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("The IP Helper API is Windows only.");

        // what Windows fixes for its loopback: the interface type in the LUID, an unlimited MTU, connected
        const int ifTypeSoftwareLoopback = 24;
        Win32IpHelper.InitializeIpInterfaceEntry(out var row);
        row.Family = (ushort)AddressFamily.InterNetwork;
        row.InterfaceIndex = (uint)NetworkInterface.LoopbackInterfaceIndex;
        Assert.AreEqual(0, Win32IpHelper.GetIpInterfaceEntry(ref row));
        Assert.AreEqual(ifTypeSoftwareLoopback, (int)(row.InterfaceLuid >> 48));
        Assert.AreEqual(uint.MaxValue, row.NlMtu);
        Assert.AreEqual(1, (int)row.Connected);
    }

    private static void AssertLayout<T>(int size, (string Field, int Offset)[] offsets)
    {
        Assert.AreEqual(size, Marshal.SizeOf<T>(), typeof(T).Name);
        foreach (var (field, offset) in offsets)
            Assert.AreEqual(offset, (int)Marshal.OffsetOf<T>(field), $"{typeof(T).Name}.{field}");

        // every field is listed, so one added or renamed fails too
        Assert.AreEqual(offsets.Length, typeof(T).GetFields().Length, typeof(T).Name);
    }
}

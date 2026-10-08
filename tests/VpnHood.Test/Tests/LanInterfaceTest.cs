using System.Net.NetworkInformation;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

// Which interfaces may hold the pairing page's address (IPAddressUtil.GetLanAddresses), judged on what
// the OS reports of each, as Windows, Linux, Android and Apple's systems name and type them. Linux's
// tun_flags reads /sys for a real interface, so only a run on Linux sees it.
[TestClass]
public class LanInterfaceTest
{
    // IF_TYPE_PROP_VIRTUAL: WinTun, WireGuard-NT and TAP
    private const NetworkInterfaceType WindowsProprietaryVirtualType = (NetworkInterfaceType)53;

    [TestMethod]
    [DataRow("Wi-Fi", NetworkInterfaceType.Wireless80211, "Intel(R) Wi-Fi 6E AX211 160MHz")]
    [DataRow("Ethernet", NetworkInterfaceType.Ethernet, "Realtek PCIe GbE Family Controller")]
    [DataRow("Ethernet", NetworkInterfaceType.Ethernet, "Microsoft Hyper-V Network Adapter")] // a Hyper-V guest's own card
    [DataRow("eth0", NetworkInterfaceType.Ethernet, "eth0")]
    [DataRow("wlan0", NetworkInterfaceType.Unknown, "wlan0")] // Android 11+ types nothing
    public void LanCardIsKept(string name, NetworkInterfaceType type, string description)
    {
        Assert.IsTrue(IsLanInterface(name, type, description));
    }

    [TestMethod]
    [DataRow("MyVpnClient", WindowsProprietaryVirtualType, "Wintun Userspace Tunnel")] // a fork's tunnel
    [DataRow("Local Area Connection", WindowsProprietaryVirtualType, "TAP-Windows Adapter V9")]
    [DataRow("tun0", NetworkInterfaceType.Unknown, "tun0")] // Android's VPN
    [DataRow("utun4", NetworkInterfaceType.Unknown, "utun4")] // Apple's
    [DataRow("Teredo Tunneling Pseudo-Interface", NetworkInterfaceType.Tunnel, "Teredo Tunneling Pseudo-Interface")]
    [DataRow("ppp0", NetworkInterfaceType.Ppp, "ppp0")]
    public void TunnelIsLeftOutWhateverItsName(string name, NetworkInterfaceType type, string description)
    {
        Assert.IsFalse(IsLanInterface(name, type, description));
    }

    [TestMethod]
    [DataRow("Loopback Pseudo-Interface 1", NetworkInterfaceType.Loopback, "Software Loopback Interface 1")]
    [DataRow("lo", NetworkInterfaceType.Loopback, "lo")] // where WSL puts 10.255.255.254
    [DataRow("Cellular", NetworkInterfaceType.Wwanpp, "Generic Mobile Broadband Adapter")]
    [DataRow("Cellular 2", NetworkInterfaceType.Wwanpp2, "Generic Mobile Broadband Adapter")]
    [DataRow("vEthernet (Default Switch)", NetworkInterfaceType.Ethernet, "Hyper-V Virtual Ethernet Adapter")]
    [DataRow("Ethernet 3", NetworkInterfaceType.Ethernet, "Hyper-V Virtual Ethernet Adapter")] // a renamed host vNIC
    [DataRow("VirtualBox Host-Only Network", NetworkInterfaceType.Ethernet, "VirtualBox Host-Only Ethernet Adapter")]
    public void UnreachableInterfaceIsLeftOut(string name, NetworkInterfaceType type, string description)
    {
        Assert.IsFalse(IsLanInterface(name, type, description));
    }

    [TestMethod]
    public void DownInterfaceIsLeftOut()
    {
        var networkInterface = new TestNetworkInterface("Ethernet", NetworkInterfaceType.Ethernet,
            "Realtek PCIe GbE Family Controller", OperationalStatus.Down);

        Assert.IsFalse(IPAddressUtil.IsLanInterface(networkInterface, IPAddressUtil.VirtualAdapterMarkers));
    }

    [TestMethod]
    public void LinuxMobileBroadbandIsLeftOutByName()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Inconclusive("Linux names its mobile broadband; elsewhere the type says it.");

        Assert.IsFalse(IsLanInterface("wwan0", NetworkInterfaceType.Ethernet, "wwan0"));
        Assert.IsFalse(IsLanInterface("wwp0s20f0u3i12", NetworkInterfaceType.Unknown, "wwp0s20f0u3i12"));
        Assert.IsTrue(IsLanInterface("wlp2s0", NetworkInterfaceType.Wireless80211, "wlp2s0"));
    }

    private static bool IsLanInterface(string name, NetworkInterfaceType type, string description)
    {
        return IPAddressUtil.IsLanInterface(new TestNetworkInterface(name, type, description),
            IPAddressUtil.VirtualAdapterMarkers);
    }
}

using System.Net;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.VpnAdapters.LinuxTun;

namespace VpnHood.Test.Tests;

// What of the Linux adapter runs on any system: its checks and the commands it makes. What needs a TUN device, root
// and iptables is checked on Linux by hand.
[TestClass]
public class LinuxTunVpnAdapterTest
{
    [TestMethod]
    public void An_adapter_says_it_can_protect_a_socket_before_its_first_start()
    {
        // the client asks once, when it is made, before any start: told no, it never bound a socket out of the tunnel
        using var adapter = new LinuxTunVpnAdapter(new LinuxVpnAdapterSettings {
            AdapterName = "vh-test",
            Blocking = false
        });
        Assert.IsTrue(adapter.CanProtectSocket);
    }

    [TestMethod]
    [DataRow("nameserver 2001:db8:0:0:0:0:0:1\n", true)]
    [DataRow("nameserver 2001:DB8::1\n", true)]
    [DataRow("nameserver 2001:db8::1\nnameserver 127.0.0.53\n", true)]
    [DataRow("nameserver 2001:db8::2\n", false)]
    [DataRow("nameserver not-an-address\n", false)]
    public void A_server_in_resolv_conf_is_known_by_its_address_not_by_its_text(string resolvConf, bool expected)
    {
        Assert.AreEqual(expected,
            LinuxTunVpnAdapter.IsResolvConfOnVpnDns(resolvConf, [IPAddress.Parse("2001:db8::1")]));
    }

    [TestMethod]
    public void An_untagged_tun_that_is_down_is_not_taken_for_the_apps_own()
    {
        var untagged = new LinuxTunInfo("vh-test", "", IsHeld: false, IsDown: false);

        // up, and no process holds it: one a version before tags left
        Assert.IsTrue(LinuxTunVpnAdapter.IsOwnLeftover(untagged, "vh-test", "com.example.app"));

        // down: whether a process holds it is not known, and with no tag nothing says it is this app's
        Assert.IsFalse(LinuxTunVpnAdapter.IsOwnLeftover(untagged with { IsDown = true }, "vh-test", "com.example.app"));
    }

    [TestMethod]
    public void NAT_leaves_by_the_interface_of_a_default_route()
    {
        Assert.AreEqual("eth0", LinuxTunVpnAdapter.ParseDefaultRouteInterface(
            "default via 192.168.1.1 dev eth0 proto dhcp src 192.168.1.20 metric 100 \n"));
        Assert.AreEqual("ens3", LinuxTunVpnAdapter.ParseDefaultRouteInterface(
            "default via fe80::1 dev ens3 proto ra metric 100 expires 1795sec pref medium\n"));
        Assert.AreEqual("ppp0", LinuxTunVpnAdapter.ParseDefaultRouteInterface("default dev ppp0 scope link \n"));

        // no route of the IP version, as IPv4's on a host with IPv6 alone
        Assert.ThrowsExactly<InvalidOperationException>(() => LinuxTunVpnAdapter.ParseDefaultRouteInterface(""));

        // a multipath route, which the rules' one interface cannot cover
        Assert.ThrowsExactly<InvalidOperationException>(() => LinuxTunVpnAdapter.ParseDefaultRouteInterface(
            "default proto ra metric 1024 pref medium\\\tnexthop via fe80::1 dev eth0 weight 1 " +
            "\\\tnexthop via fe80::2 dev eth1 weight 1 \n"));

        // a name that no command of root's may carry
        Assert.ThrowsExactly<InvalidOperationException>(() => LinuxTunVpnAdapter.ParseDefaultRouteInterface(
            "default via 10.0.0.1 dev eth0;reboot proto static\n"));
    }

    [TestMethod]
    public void NAT_is_made_in_chains_of_the_adapters_own()
    {
        // each chain is filled before the jump that leads to it; no rule goes among the other tools' rules
        var commands = LinuxTunVpnAdapter.CreateNatCommands("vh-test", IpNetwork.Parse("10.255.0.0/16"), "eth0");
        CollectionAssert.AreEqual(new[] {
            "iptables -w 5 -t nat -N vpnhood_nat_vh-test",
            "iptables -w 5 -t nat -A vpnhood_nat_vh-test -s 10.255.0.0/16 -o eth0 -j MASQUERADE",
            "iptables -w 5 -t nat -A POSTROUTING -j vpnhood_nat_vh-test",
            "iptables -w 5 -N vpnhood_nat_vh-test",
            "iptables -w 5 -A vpnhood_nat_vh-test -i vh-test -o eth0 -j ACCEPT",
            "iptables -w 5 -A vpnhood_nat_vh-test -i eth0 -o vh-test -m state --state RELATED,ESTABLISHED -j ACCEPT",
            "iptables -w 5 -A FORWARD -j vpnhood_nat_vh-test"
        }, commands.ToArray());

        // an IPv6 network's are ip6tables'; the longest adapter name still makes a chain name iptables takes
        var longName = new string('a', 15);
        var commandsV6 = LinuxTunVpnAdapter.CreateNatCommands(longName, IpNetwork.Parse("fd00::/64"), "eth0");
        Assert.IsTrue(commandsV6.All(x => x.StartsWith("ip6tables -w 5 ", StringComparison.Ordinal)));
        Assert.IsTrue(commandsV6[0].EndsWith($" -N vpnhood_nat_{longName}", StringComparison.Ordinal));
        Assert.IsTrue($"vpnhood_nat_{longName}".Length <= 28);
    }
}

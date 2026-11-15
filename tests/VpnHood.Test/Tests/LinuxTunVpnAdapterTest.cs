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
    public void The_links_DNS_domains_are_read_as_resolvectl_lists_them()
    {
        // as systemd 259 prints them: a search domain as it is, a routing domain with ~, a character of a label
        // that is no letter as a backslash and three digits, and a link with none ending at its colon
        const string list =
            "Global:\n" +
            "Link 2 (eth0): lan ~corp.example\n" +
            "Link 3 (vh-test): ~.\n" +
            "Link 4 (docker0):\n" +
            "Link 12 (wl(an)0): a\\032b.example\n" +
            "Link 1000000000 (veth9): corp.example\n";

        var links = LinuxLinkDomains.ParseAll(list);
        Assert.HasCount(5, links);
        Assert.AreEqual(2, links[0].Index);
        Assert.AreEqual("eth0", links[0].Name);
        CollectionAssert.AreEqual(new[] { "lan", "~corp.example" }, links[0].Domains.ToArray());
        CollectionAssert.AreEqual(new[] { "~." }, links[1].Domains.ToArray());
        Assert.IsEmpty(links[2].Domains);
        Assert.AreEqual(12, links[3].Index);
        Assert.AreEqual("wl(an)0", links[3].Name);
        Assert.AreEqual(1000000000, links[4].Index);
        Assert.IsFalse(links.Any(x => x.IsManagedByNetworkd));

        // the adapter's record is the same lines, so what it took off a link is read back as it was
        var record = string.Join('\n', links.Select(x => x.ToLine())) + "\n";
        Assert.AreEqual(list["Global:\n".Length..], record);

        // the record alone marks a link that systemd-networkd managed
        var managed = LinuxLinkDomains.ParseAll("ManagedLink 2 (eth0): lan\n").Single();
        Assert.IsTrue(managed.IsManagedByNetworkd);
        Assert.AreEqual("ManagedLink 2 (eth0): lan", managed.ToLine());
    }

    [TestMethod]
    [DataRow("Link 2 (eth0): lan'; reboot; '\n")]
    [DataRow("Link 2 (eth0): $(reboot).example\n")]
    [DataRow("Link 2 (eth0): lan `reboot`\n")]
    [DataRow("Link two (eth0): lan\n")]
    [DataRow("Link 0 (eth0): lan\n")]
    [DataRow("Link 99999999999 (eth0): lan\n")]
    [DataRow("Link 2 eth0: lan\n")]
    public void A_links_line_that_resolvectl_would_not_write_is_refused(string list)
    {
        // A domain goes onto root's command line when it is given back, between single quotes. And a link's line
        // that is passed over is a link whose domains stay, unseen
        Assert.ThrowsExactly<FormatException>(() => LinuxLinkDomains.ParseAll(list));
    }

    [TestMethod]
    [DataRow("nameserver 127.0.0.53\noptions edns0 trust-ad\n", true)]
    [DataRow("nameserver 127.0.0.54\n", true)]
    [DataRow("nameserver 10.255.0.1\n", false)]
    [DataRow("", false)]
    public void Systemd_resolved_answers_where_resolv_conf_names_its_stub(string resolvConf, bool expected)
    {
        // The other links' domains are taken off where systemd-resolved answers the host's lookups, whichever
        // command took the VPN's servers: a resolvconf may be systemd-resolved's own
        Assert.AreEqual(expected, LinuxTunVpnAdapter.IsResolvConfOnResolved(resolvConf));
    }

    [TestMethod]
    [DataRow("ADMIN_STATE=configured\nOPER_STATE=routable\n", true)]
    [DataRow("ADMIN_STATE=configuring\n", true)]
    [DataRow("ADMIN_STATE=failed\n", true)]
    [DataRow("ADMIN_STATE=unmanaged\nOPER_STATE=routable\n", false)]
    [DataRow("ADMIN_STATE=pending\n", false)]
    [DataRow("ADMIN_STATE=initialized\n", false)]
    [DataRow("OPER_STATE=routable\n", false)]
    public void A_link_is_systemd_networkds_by_its_setup_state(string linkState, bool expected)
    {
        // such a link gets its DNS domains back by a revert, any other by the domains themselves: the states are
        // the ones systemd-resolved goes by, and networkd writes the file for a link it leaves alone too
        Assert.AreEqual(expected, LinuxTunVpnAdapter.IsManagedByNetworkd(linkState));
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

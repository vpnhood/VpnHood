using System.Runtime.InteropServices;
using VpnHood.Net.VpnAdapters.LinuxTun;
using VpnHood.Test.Device;

namespace VpnHood.Test.Tests;

// The Linux adapter's taking of the other links' DNS domains and its giving them back, on a list of links of the
// test's own. What systemd-resolved itself does with the commands is checked on Linux by hand.
[TestClass]
public class LinuxLinkDomainsKeeperTest : TestBase
{
    private const string AdapterName = "vh-test";

    private string CreateRecordPath()
    {
        return Path.Combine(TestHelper.WorkingPath, $"link-domains-{Guid.NewGuid():N}", $"{AdapterName}.link-domains");
    }

    private static LinuxLinkDomainsKeeper CreateKeeper(TestResolvedLinks resolved, string recordPath)
    {
        return new LinuxLinkDomainsKeeper(AdapterName, recordPath, resolved.Execute, resolved.IsManagedByNetworkd);
    }

    [TestMethod]
    public void The_other_links_lose_their_domains_and_get_them_back()
    {
        var resolved = new TestResolvedLinks();
        var eth0 = resolved.Add(2, "eth0", "lan", "~corp.example");
        var tun = resolved.Add(3, AdapterName, "~.");
        var docker0 = resolved.Add(4, "docker0");
        var recordPath = CreateRecordPath();
        var keeper = CreateKeeper(resolved, recordPath);

        // the adapter's own link keeps its domain, and what is taken is recorded before it goes
        keeper.Take();
        Assert.IsEmpty(eth0.Domains);
        Assert.IsEmpty(docker0.Domains);
        CollectionAssert.AreEqual(new[] { "~." }, tun.Domains);
        Assert.AreEqual("Link 2 (eth0): lan ~corp.example\n", File.ReadAllText(recordPath));

        keeper.GiveBack();
        CollectionAssert.AreEqual(new[] { "lan", "~corp.example" }, eth0.Domains);
        CollectionAssert.AreEqual(new[] { "~." }, tun.Domains);
        Assert.IsFalse(File.Exists(recordPath));
    }

    [TestMethod]
    public void A_link_that_keeps_its_domains_fails_the_taking()
    {
        // as systemd 245 and 249 for a link systemd-networkd manages: the command says nothing is wrong, and the
        // domain is still there
        var resolved = new TestResolvedLinks();
        var eth0 = resolved.Add(2, "eth0", "lan");
        var managed = resolved.AddManaged(5, "ens5", ignoresEmptyList: true, "corp.example");
        var recordPath = CreateRecordPath();
        var keeper = CreateKeeper(resolved, recordPath);

        var ex = Assert.ThrowsExactly<InvalidOperationException>(keeper.Take);
        Assert.Contains("ens5", ex.Message);
        Assert.Contains("outside the VPN", ex.Message);
        CollectionAssert.AreEqual(new[] { "corp.example" }, managed.Domains);

        // what the failed taking took off the other link goes back
        Assert.IsEmpty(eth0.Domains);
        keeper.GiveBack();
        CollectionAssert.AreEqual(new[] { "lan" }, eth0.Domains);
        CollectionAssert.AreEqual(new[] { "corp.example" }, managed.Domains);
        Assert.IsFalse(File.Exists(recordPath));
    }

    [TestMethod]
    public void A_link_that_kept_its_domains_is_asked_again_only_once_they_change()
    {
        // The command again would have the system write the link's settings anew, which the adapter's watch tells of,
        // and the look that follows would send it again, with no end
        var resolved = new TestResolvedLinks();
        var managed = resolved.AddManaged(5, "ens5", ignoresEmptyList: true, "corp.example");
        var keeper = CreateKeeper(resolved, CreateRecordPath());
        Assert.ThrowsExactly<InvalidOperationException>(keeper.Take);

        resolved.Commands.Clear();
        keeper.Take();
        CollectionAssert.AreEqual(new[] { "resolvectl -- domain" }, resolved.Commands);

        // a link that got another domain since, as from its network, is asked again
        var eth0 = resolved.Add(2, "eth0", "lan");
        managed.Domains = ["corp.example", "lab.example"];
        Assert.ThrowsExactly<InvalidOperationException>(keeper.Take);
        Assert.Contains("resolvectl -- domain 5 ''", resolved.Commands);
        Assert.IsEmpty(eth0.Domains);
    }

    [TestMethod]
    public void A_link_that_kept_its_domains_is_remembered_when_another_links_command_fails()
    {
        // a failure thrown at once skipped the remembering, so the kept link was asked again at every look
        var resolved = new TestResolvedLinks { FailingLinkIndex = 6 };
        resolved.AddManaged(5, "ens5", ignoresEmptyList: true, "corp.example");
        resolved.Add(6, "ens6", "lab.example");
        var keeper = CreateKeeper(resolved, CreateRecordPath());
        var ex = Assert.ThrowsExactly<AggregateException>(keeper.Take);
        Assert.HasCount(2, ex.InnerExceptions);

        // the failed command is tried again, as it wrote nothing; the kept link is not
        resolved.Commands.Clear();
        Assert.ThrowsExactly<InvalidOperationException>(keeper.Take);
        Assert.DoesNotContain("resolvectl -- domain 5 ''", resolved.Commands);
        Assert.Contains("resolvectl -- domain 6 ''", resolved.Commands);
    }

    [TestMethod]
    public void A_command_that_fails_fails_the_taking()
    {
        var resolved = new TestResolvedLinks { FailingLinkIndex = 2 };
        resolved.Add(2, "eth0", "lan");
        var keeper = CreateKeeper(resolved, CreateRecordPath());

        var ex = Assert.ThrowsExactly<InvalidOperationException>(keeper.Take);
        Assert.IsInstanceOfType<ExternalException>(ex.InnerException);
        Assert.Contains("eth0", ex.Message);
    }

    [TestMethod]
    public void A_giving_back_that_fails_keeps_its_link_recorded()
    {
        var resolved = new TestResolvedLinks();
        var eth0 = resolved.Add(2, "eth0", "lan");
        var wlan0 = resolved.Add(6, "wlan0", "home.example");
        var recordPath = CreateRecordPath();
        var keeper = CreateKeeper(resolved, recordPath);
        keeper.Take();

        // One link's command fails: the other gets its domains, and the record keeps the one that did not. Deleted
        // with the rest, nothing would be left to give that link its domains back from
        resolved.FailingLinkIndex = 2;
        keeper.GiveBack();
        Assert.IsEmpty(eth0.Domains);
        CollectionAssert.AreEqual(new[] { "home.example" }, wlan0.Domains);
        Assert.AreEqual("Link 2 (eth0): lan\n", File.ReadAllText(recordPath));

        // the next try, as the app's next start makes
        resolved.FailingLinkIndex = null;
        keeper.GiveBack();
        CollectionAssert.AreEqual(new[] { "lan" }, eth0.Domains);
        Assert.IsFalse(File.Exists(recordPath));
    }

    [TestMethod]
    public void A_record_that_cannot_be_read_stops_the_next_taking()
    {
        var resolved = new TestResolvedLinks();
        resolved.Add(2, "eth0", "lan");
        var recordPath = CreateRecordPath();
        var keeper = CreateKeeper(resolved, recordPath);
        keeper.Take();

        // Another link comes with a domain, as after a change of the network, and the record is not what it was.
        // Taken as empty and written anew, it would lose the first link's domains
        var wlan0 = resolved.Add(6, "wlan0", "home.example");
        var record = File.ReadAllText(recordPath) + "Link 9 (eth9): not'a'domain\n";
        File.WriteAllText(recordPath, record);
        Assert.ThrowsExactly<FormatException>(keeper.Take);
        CollectionAssert.AreEqual(new[] { "home.example" }, wlan0.Domains);
        Assert.AreEqual(record, File.ReadAllText(recordPath));

        // such a record gives nothing back, and does not stay to fail every start
        keeper.GiveBack();
        Assert.IsFalse(File.Exists(recordPath));
    }

    [TestMethod]
    public void A_link_of_systemd_networkd_goes_back_to_it()
    {
        // as systemd 255 and later: the domains go, by a setting at run time that stands until a revert
        var resolved = new TestResolvedLinks();
        var eth0 = resolved.AddManaged(2, "eth0", ignoresEmptyList: false, "corp.example");
        var recordPath = CreateRecordPath();
        var isGivingBack = false;
        var keeper = new LinuxLinkDomainsKeeper(AdapterName, recordPath, resolved.Execute, linkIndex =>
            !isGivingBack ? resolved.IsManagedByNetworkd(linkIndex) : throw new InvalidOperationException(
                "Test: whose the link was is known from the record, as networkd may be down by now."));

        keeper.Take();
        Assert.IsEmpty(eth0.Domains);
        Assert.AreEqual("ManagedLink 2 (eth0): corp.example\n", File.ReadAllText(recordPath));

        // networkd gets another domain for the link meanwhile: given its recorded domain by name, the link would
        // keep that one through this and every later change
        eth0.NetworkdDomains = ["new.example"];
        isGivingBack = true;
        keeper.GiveBack();
        Assert.AreEqual("resolvectl -- revert 2", resolved.Commands[^1]);
        CollectionAssert.AreEqual(new[] { "new.example" }, eth0.Domains);
        Assert.IsFalse(File.Exists(recordPath));
    }

    [TestMethod]
    public void A_renamed_link_gets_its_domains_back()
    {
        var resolved = new TestResolvedLinks();
        var eth0 = resolved.Add(2, "eth0", "lan");
        var recordPath = CreateRecordPath();
        var keeper = CreateKeeper(resolved, recordPath);
        keeper.Take();

        // its index is what a link keeps
        eth0.Name = "uplink0";
        keeper.GiveBack();
        CollectionAssert.AreEqual(new[] { "lan" }, eth0.Domains);
        Assert.IsFalse(File.Exists(recordPath));
    }

    [TestMethod]
    public void A_link_whose_manager_set_its_domains_again_keeps_them()
    {
        var resolved = new TestResolvedLinks();
        var eth0 = resolved.Add(2, "eth0", "lan");
        var gone = resolved.Add(7, "usb0", "tether.example");
        var recordPath = CreateRecordPath();
        var keeper = CreateKeeper(resolved, recordPath);
        keeper.Take();

        // one link got other domains from its own manager since, and one is gone: neither is touched
        eth0.Domains = ["office.example"];
        resolved.Remove(gone);
        resolved.Commands.Clear();
        keeper.GiveBack();
        CollectionAssert.AreEqual(new[] { "office.example" }, eth0.Domains);
        CollectionAssert.AreEqual(new[] { "resolvectl -- domain" }, resolved.Commands);
        Assert.IsFalse(File.Exists(recordPath));
    }

    [TestMethod]
    public void A_later_taking_keeps_the_earlier_links_recorded()
    {
        var resolved = new TestResolvedLinks();
        var eth0 = resolved.Add(2, "eth0", "lan");
        var recordPath = CreateRecordPath();
        var keeper = CreateKeeper(resolved, recordPath);
        keeper.Take();

        // after a change of the network: a new link with a domain, and the first link with another one
        var wlan0 = resolved.Add(6, "wlan0", "home.example");
        keeper.Take();
        Assert.AreEqual("Link 2 (eth0): lan\nLink 6 (wlan0): home.example\n", File.ReadAllText(recordPath));

        eth0.Domains = ["office.example"];
        keeper.Take();
        Assert.AreEqual("Link 6 (wlan0): home.example\nLink 2 (eth0): office.example\n", File.ReadAllText(recordPath));

        keeper.GiveBack();
        CollectionAssert.AreEqual(new[] { "office.example" }, eth0.Domains);
        CollectionAssert.AreEqual(new[] { "home.example" }, wlan0.Domains);
    }

    [TestMethod]
    public void A_domain_that_begins_with_a_dash_is_no_option()
    {
        // a DNS name may be "--help", and resolvectl took that word for its own option: it printed its help, said
        // nothing was wrong, and set no domain
        var resolved = new TestResolvedLinks();
        var eth0 = resolved.Add(2, "eth0", "--help", "~-x.example");
        var keeper = CreateKeeper(resolved, CreateRecordPath());
        keeper.Take();
        keeper.GiveBack();
        Assert.AreEqual("resolvectl -- domain 2 '--help' '~-x.example'", resolved.Commands[^1]);
        CollectionAssert.AreEqual(new[] { "--help", "~-x.example" }, eth0.Domains);
    }

    [TestMethod]
    public void A_link_is_named_by_its_index()
    {
        // the index needs no quoting, where a name may hold what a command line reads; no domain is the empty word
        Assert.AreEqual("resolvectl -- domain 2 ''", LinuxLinkDomainsKeeper.SetCommand(2, []));
        Assert.AreEqual("resolvectl -- domain 2 'lan' '~corp.example' 'a\\032b.example'",
            LinuxLinkDomainsKeeper.SetCommand(2, ["lan", "~corp.example", "a\\032b.example"]));
    }

    [TestMethod]
    public void Nothing_is_recorded_where_no_link_has_a_domain()
    {
        var resolved = new TestResolvedLinks();
        resolved.Add(2, "eth0");
        resolved.Add(3, AdapterName, "~.");
        var recordPath = CreateRecordPath();
        var keeper = CreateKeeper(resolved, recordPath);

        keeper.Take();
        keeper.GiveBack();
        Assert.IsFalse(File.Exists(recordPath));
        CollectionAssert.AreEqual(new[] { "resolvectl -- domain" }, resolved.Commands);
    }
}

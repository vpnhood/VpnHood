using VpnHood.Net.VpnAdapters.LinuxTun;

namespace VpnHood.Test.Tests;

// The Linux adapter's name as the kernel will take it. Pure string work, so it runs anywhere.
[TestClass]
public class LinuxTunAdapterNameTest
{
    [TestMethod]
    [DataRow("VpnHoodClient")]
    [DataRow("VpnHoodConnect")]
    [DataRow("VpnHoodServer")]
    [DataRow("tun0")]
    [DataRow("a.b_c-d")]
    [DataRow("fifteen_chars_x")]
    public void ValidNamePassesUnchanged(string name)
    {
        Assert.IsTrue(LinuxTunVpnAdapter.IsValidAdapterName(name));
        Assert.AreEqual(name, LinuxTunVpnAdapter.GetValidAdapterName(name));
    }

    [TestMethod]
    [DataRow("VpnHOODClient_dbg")] // a debug build's name: 17 characters
    [DataRow("VpnHood! CLIENT")]
    [DataRow("-leading-dash")]
    [DataRow("..")]
    [DataRow("a/b")]
    [DataRow("a:b")]
    [DataRow("")]
    public void InvalidNameIsDerivedToAValidOne(string name)
    {
        Assert.IsFalse(LinuxTunVpnAdapter.IsValidAdapterName(name));

        var derived = LinuxTunVpnAdapter.GetValidAdapterName(name);
        Assert.IsTrue(LinuxTunVpnAdapter.IsValidAdapterName(derived), $"'{derived}' is not valid.");
        Assert.AreEqual(derived, LinuxTunVpnAdapter.GetValidAdapterName(name), "The derivation must be stable.");
    }

    [TestMethod]
    public void DerivedNameKeepsTenValidCharactersAndAHash()
    {
        var derived = LinuxTunVpnAdapter.GetValidAdapterName("VpnHOODClient_dbg");

        StringAssert.StartsWith(derived, "VpnHOODCli-");
        Assert.AreEqual(15, derived.Length);
    }

    [TestMethod]
    public void LongNamesSharingAPrefixDeriveDifferently()
    {
        Assert.AreNotEqual(
            LinuxTunVpnAdapter.GetValidAdapterName("VpnHoodClientOne_dbg"),
            LinuxTunVpnAdapter.GetValidAdapterName("VpnHoodClientTwo_dbg"));
    }
}

using VpnHood.Net.VpnAdapters.LinuxTun;

namespace VpnHood.Test.Tests;

[TestClass]
public class LinuxTunAdapterAliasTest
{
    [TestMethod]
    public void ExistingAliasesRemainCompatible()
    {
        foreach (var appId in new[] { "com.vpnhood.client", "app:id/path_1-2", new string('a', 255) })
            Assert.AreEqual(appId, LinuxTunVpnAdapter.GetAdapterAlias(appId));
    }

    [TestMethod]
    [DataRow(256)]
    [DataRow(10000)]
    public void LongIdsHaveStableBoundedAliases(int length)
    {
        var appId = new string('a', length);
        var alias = LinuxTunVpnAdapter.GetAdapterAlias(appId);

        Assert.AreEqual(64 + 1 + 16, alias.Length);
        StringAssert.StartsWith(alias, new string('a', 64) + "-");
        Assert.AreEqual(alias, LinuxTunVpnAdapter.GetAdapterAlias(appId));
        Assert.AreNotEqual(alias, LinuxTunVpnAdapter.GetAdapterAlias(appId + "b"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("app name\twith\nwhitespace")]
    [DataRow("应用程序/شبكة/🚀")]
    [DataRow("app'; $(echo unexpected) `echo unexpected` \" & | > < \\ \0")]
    public void ArbitraryIdsProduceNonemptyCommandSafeAliases(string appId)
    {
        var alias = LinuxTunVpnAdapter.GetAdapterAlias(appId);

        Assert.IsTrue(alias.Length is > 0 and <= 255);
        Assert.IsTrue(alias.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or ':' or '/'));
        Assert.AreEqual(alias, LinuxTunVpnAdapter.GetAdapterAlias(appId));
    }

    [TestMethod]
    public void SanitizingAnIdDoesNotDiscardItsIdentity()
    {
        Assert.AreNotEqual(LinuxTunVpnAdapter.GetAdapterAlias("app one"),
            LinuxTunVpnAdapter.GetAdapterAlias("app?one"));
    }

    [TestMethod]
    public void DerivedAliasesIdentifyOnlyUnheldLeftoversOfTheSameApp()
    {
        var appId = new string('a', 1000);
        var alias = LinuxTunVpnAdapter.GetAdapterAlias(appId);
        var own = new LinuxTunInfo("old-tun", alias, false);
        var foreign = own with { Alias = LinuxTunVpnAdapter.GetAdapterAlias(appId + "other") };

        Assert.IsTrue(LinuxTunVpnAdapter.IsOwnLeftover(own, "new-tun", alias));
        Assert.IsFalse(LinuxTunVpnAdapter.IsOwnLeftover(own with { IsHeld = true }, "old-tun", alias));
        Assert.IsFalse(LinuxTunVpnAdapter.IsOwnLeftover(foreign, "old-tun", alias));
        Assert.IsFalse(LinuxTunVpnAdapter.IsOwnLeftover(own, "old-tun", null));
    }
}

using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Cli.Commands;

namespace VpnHood.AppLib.Test.Tests;

// "service log -f" over the API's answers: the app's part and the VPN service's, each followed apart
// as it grows, and printed again from the top when it starts again.
[TestClass]
public class ServiceLogFollowerTest
{
    private static readonly string AppHeader = Header(VpnHoodApp.LogAppHeader);
    private static readonly string ServiceHeader = Header(VpnHoodApp.LogVpnServiceHeader);

    private static string Header(string name, string newLine = "\n")
    {
        return $"-----------------------{newLine}{name}{newLine}-----------------------{newLine}";
    }

    // As VpnHoodApp.CopyLogToStream writes it: an empty line between the parts.
    private static string Answer(string app, string? vpnService = null)
    {
        return AppHeader + app + (vpnService == null ? "" : "\n" + ServiceHeader + vpnService);
    }

    [TestMethod]
    public void The_first_answer_is_each_parts_header_and_last_lines()
    {
        var follower = new ServiceLogFollower();

        var text = follower.First(Answer("a1\na2\na3\n", "s1\ns2\ns3\n"), lines: 2);

        Assert.AreEqual(AppHeader + "a2\na3\n" + ServiceHeader + "s2\ns3\n", text);
    }

    [TestMethod]
    public void The_empty_line_ending_an_entry_is_not_its_last_line()
    {
        var follower = new ServiceLogFollower();

        var text = follower.First(Answer("10:00 | Info |\na1\n\n10:01 | Info |\na2\n\n"), lines: 1);

        Assert.AreEqual(AppHeader + "a2\n\n", text);
    }

    [TestMethod]
    public void Each_part_is_followed_apart_under_its_header()
    {
        var follower = new ServiceLogFollower();
        follower.First(Answer("a1\n", "s1\n"), lines: 10);

        Assert.AreEqual(AppHeader + "a2\n", follower.Next(Answer("a1\na2\n", "s1\n")));
        Assert.AreEqual(ServiceHeader + "s2\n", follower.Next(Answer("a1\na2\n", "s1\ns2\n")));
        Assert.AreEqual("s3\n", follower.Next(Answer("a1\na2\n", "s1\ns2\ns3\n")));
        Assert.AreEqual("", follower.Next(Answer("a1\na2\n", "s1\ns2\ns3\n")));
    }

    [TestMethod]
    public void A_part_that_starts_again_is_printed_from_the_top()
    {
        var follower = new ServiceLogFollower();
        follower.First(Answer("a1\n", "s1\ns2\n"), lines: 10);

        Assert.AreEqual(ServiceHeader + "t1\n", follower.Next(Answer("a1\n", "t1\n")));
        Assert.AreEqual(AppHeader + "b1\n", follower.Next(Answer("b1\n", "t1\n")));
    }

    [TestMethod]
    public void The_vpn_services_part_coming_later_leaves_the_apps_as_it_was()
    {
        var follower = new ServiceLogFollower();
        follower.First(Answer("a1\n"), lines: 10);

        Assert.AreEqual(ServiceHeader + "s1\n", follower.Next(Answer("a1\n", "s1\n")));
    }

    [TestMethod]
    public void Windows_line_breaks_split_the_same_way()
    {
        var follower = new ServiceLogFollower();
        var appHeader = Header(VpnHoodApp.LogAppHeader, "\r\n");
        var serviceHeader = Header(VpnHoodApp.LogVpnServiceHeader, "\r\n");

        var text = follower.First(appHeader + "a1\r\n\r\n" + serviceHeader + "s1\r\n", lines: 10);

        Assert.AreEqual(appHeader + "a1\r\n" + serviceHeader + "s1\r\n", text);
    }
}

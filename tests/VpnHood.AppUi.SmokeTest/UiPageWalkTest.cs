using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VpnHood.AppUi.SmokeTest;

// A vibe check on the Avalonia UI: open every page a walk can reach without a server, and say so
// when one does not open, opens the wrong thing, or brings an error dialog with it. It asserts
// what a person notices in a second - did the page I asked for appear - and not how it looks;
// pictures are written beside the results for a person to judge that part.
//
// It drives a dev head with its own storage, so an installed client is never touched, and it is
// kept out of the normal run by its category. See README.md in this folder.
//
// The page titles below are the English ones. A machine whose UI language is not English will
// fail on the title, not on the app.
[TestClass]
[TestCategory(Category)]
[DoNotParallelize]
public class UiPageWalkTest
{
    public const string Category = "Ui";

    private static DevHeadSession? _session;
    private static bool _termsWereShown;
    private static string _pictureFolder = "";

    private static DevHeadSession Session =>
        _session ?? throw new InvalidOperationException("The head was not started.");

    [ClassInitialize]
    public static void StartHead(TestContext testContext)
    {
        // a window walk needs a desktop to draw on; a service or a locked agent has none
        if (!Environment.UserInteractive)
            Assert.Inconclusive("A window walk needs an interactive desktop.");

        _pictureFolder = Path.Combine(testContext.TestRunResultsDirectory ?? Path.GetTempPath(), "ui-walk");
        _session = DevHeadSession.Start();

        // a fresh storage means a first run, so the terms come before anything else
        _termsWereShown = Session.Driver.TryFind("AcceptButton", TimeSpan.FromSeconds(30)) != null;
        Session.Driver.TrySaveScreenshot(Path.Combine(_pictureFolder, "00-terms.png"));
        if (_termsWereShown)
            Session.Driver.Invoke("AcceptButton");

        // the home is where every case below starts
        Session.Driver.Find("ConnectButton");
        Session.Driver.TrySaveScreenshot(Path.Combine(_pictureFolder, "01-home.png"));
    }

    [ClassCleanup]
    public static void StopHead()
    {
        _session?.Dispose();
        _session = null;
    }

    [TestMethod]
    public void FirstRunAsksToAcceptTheTerms()
    {
        Assert.IsTrue(_termsWereShown,
            "A first run showed no terms page, which is the one screen a new install must not skip.");
    }

    [TestMethod]
    public void HomeShowsItsRows()
    {
        Session.Driver.ReturnHome();
        foreach (var name in new[] { "ConnectButton", "ServersButton", "SplitCountriesButton", "SplitAppsButton", "ProtocolButton" })
            Assert.IsNotNull(Session.Driver.TryFind(name, TimeSpan.FromSeconds(5)), $"The home lost '{name}'.");
    }

    // The location row is missing on purpose: with no server profile it raises a notice instead of
    // opening a page, so a fresh install cannot reach it. See README.md.
    [DataTestMethod]
    [DataRow("SplitCountriesButton", "Split Countries")]
    [DataRow("SplitAppsButton", "Split Apps")]
    [DataRow("ProtocolButton", "Protocols")]
    public void HomeRowOpensItsPage(string control, string expectedTitle)
    {
        Session.Driver.ReturnHome();
        OpenPage(control, expectedTitle);
    }

    [TestMethod]
    public void DrawerOpensAndShowsItsItems()
    {
        Session.Driver.ReturnHome();
        Session.Driver.Invoke("MenuButton");

        Assert.IsNotNull(Session.Driver.TryFind("SettingsItem", TimeSpan.FromSeconds(5)), "The drawer has no Settings.");
        Assert.IsNotNull(Session.Driver.TryFind("PrivacyItem", TimeSpan.FromSeconds(5)), "The drawer has no Privacy Policy.");
        Session.Driver.TrySaveScreenshot(Path.Combine(_pictureFolder, "02-drawer.png"));
        AssertNoErrorDialog("the drawer");
    }

    [TestMethod]
    public void DrawerOpensTheSettingsPage()
    {
        OpenSettings();
    }

    [DataTestMethod]
    [DataRow("LanguageItem", "Language")]
    [DataRow("ProxiesItem", "Proxies")]
    [DataRow("SplitTunnelingItem", "Split Tunneling")]
    [DataRow("DnsItem", "DNS")]
    [DataRow("PrivacyItem", "Privacy")]
    public void SettingsRowOpensItsPage(string control, string expectedTitle)
    {
        OpenSettings();
        OpenPage(control, expectedTitle);
    }

    [TestMethod]
    public void TheWalkCaughtNoUiError()
    {
        // what MainView.ProcessError writes whenever a page hands it an exception it did not expect
        var log = Session.ReadLog();
        var lines = log
            .Split('\n')
            .Where(x => x.Contains("The UI caught an error", StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(0, lines.Length,
            $"The UI caught {lines.Length} error(s) during the walk. See {Session.LogPath}");
    }

    private static void OpenSettings()
    {
        Session.Driver.ReturnHome();
        Session.Driver.Invoke("MenuButton");
        OpenPage("SettingsItem", "Settings");
    }

    // Open one thing and say what happened: the page must appear, it must be the one asked for,
    // and it must not bring an error dialog. The title has to CHANGE, which is how a page that
    // re-opens itself instead of moving on gets caught.
    private static void OpenPage(string control, string expectedTitle)
    {
        var driver = Session.Driver;
        var titleBefore = driver.PageTitle;
        driver.Invoke(control);

        var title = driver.WaitForPageTitleChange(titleBefore, TimeSpan.FromSeconds(15));
        driver.TrySaveScreenshot(Path.Combine(_pictureFolder, $"{control}.png"));
        AssertNoErrorDialog($"'{control}'");

        Assert.IsNotNull(title, $"'{control}' opened no page; the title stayed '{titleBefore ?? "(home)"}'.");
        Assert.AreEqual(expectedTitle, title, $"'{control}' opened the wrong page.");
    }

    private static void AssertNoErrorDialog(string what)
    {
        var error = Session.Driver.ErrorText;
        Assert.IsNull(error, $"An error dialog came up on {what}: \"{error}\"");
    }
}

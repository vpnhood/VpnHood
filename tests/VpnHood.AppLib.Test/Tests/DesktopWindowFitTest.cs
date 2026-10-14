using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppLib.Test.Tests;

// The window on the screens of the test VMs, with the frames measured there: Windows' title bar and
// borders take 16x39, GNOME's title bar 37.
[TestClass]
public class DesktopWindowFitTest
{
    // the classic UI's shape (ClassicAvaloniaApp.WindowFit)
    private static readonly DesktopWindowFitOptions ClassicUi = new() {
        PhoneSize = new VhSize(400, 700),
        LandscapeWidth = 800,
        MinLandscapeHeight = 400
    };
    private static readonly VhSize WindowsFrame = new(16, 39);

    // a UI laid out for another shape, as a fork's own UI states it
    private static readonly DesktopWindowFitOptions OwnUi = new() {
        PhoneSize = new VhSize(500, 800),
        LandscapeWidth = 1000,
        MinLandscapeHeight = 600
    };

    [TestMethod]
    public void Phone_shape_where_the_screen_has_room()
    {
        // 1080p at 100%, above the taskbar
        var placement = DesktopWindowFit.Fit(new VhRect(0, 0, 1920, 1040), WindowsFrame, ClassicUi, isTv: false);
        Assert.AreEqual(new DesktopWindowPlacement(752, 150.5, new VhSize(400, 700)), placement);
    }

    [TestMethod]
    public void Landscape_where_the_phone_shape_fits_with_little_room()
    {
        // 1366x768, above the taskbar
        var placement = DesktopWindowFit.Fit(new VhRect(0, 0, 1366, 728), WindowsFrame, ClassicUi, isTv: false);
        Assert.AreEqual(new DesktopWindowPlacement(275, 74.5, new VhSize(800, 540)), placement);
    }

    [TestMethod]
    public void Landscape_in_the_middle_of_a_work_area_off_the_corner()
    {
        // Ubuntu at 1024x768, beside the dock and below the top bar
        var placement = DesktopWindowFit.Fit(new VhRect(66, 32, 958, 736), new VhSize(0, 37), ClassicUi, isTv: false);
        Assert.AreEqual(new DesktopWindowPlacement(145, 111.5, new VhSize(800, 540)), placement);
    }

    [TestMethod]
    public void Landscape_narrows_to_the_screen_and_keeps_its_least_height_and_the_title_bar_on_it()
    {
        var placement = DesktopWindowFit.Fit(new VhRect(0, 0, 800, 400), WindowsFrame, ClassicUi, isTv: false);
        Assert.AreEqual(new DesktopWindowPlacement(0, 0, new VhSize(784, 400)), placement);
    }

    [TestMethod]
    public void Tv_keeps_its_viewport()
    {
        var placement = DesktopWindowFit.Fit(new VhRect(0, 0, 1920, 1040), WindowsFrame, ClassicUi, isTv: true);
        Assert.AreEqual(new DesktopWindowPlacement(472, 230.5, new VhSize(960, 540)), placement);
    }

    [TestMethod]
    public void A_ui_of_its_own_opens_in_its_phone_shape()
    {
        var placement = DesktopWindowFit.Fit(new VhRect(0, 0, 1920, 1040), WindowsFrame, OwnUi, isTv: false);
        Assert.AreEqual(new DesktopWindowPlacement(702, 100.5, new VhSize(500, 800)), placement);
    }

    [TestMethod]
    public void A_ui_of_its_own_takes_its_landscape()
    {
        // 1366x768: as wide as the UI asks, and its least height over the TV's 540
        var placement = DesktopWindowFit.Fit(new VhRect(0, 0, 1366, 728), WindowsFrame, OwnUi, isTv: false);
        Assert.AreEqual(new DesktopWindowPlacement(175, 44.5, new VhSize(1000, 600)), placement);
    }
}

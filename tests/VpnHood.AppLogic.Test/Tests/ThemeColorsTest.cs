using VpnHood.AppHosting.Common;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppLogic.Test.Tests;

// The colours of the chrome around the UI, which a host reads from its theme's manifest in the UI's files. The
// manifest names them in camelCase, as the store ships it: a read that matches names by case finds none, and says no
// more than a warning.
[TestClass]
public class ThemeColorsTest : TestAppBase
{
    // as VpnHood.AppUi.Assets.Classic ships themes/blue/manifest.json
    private const string BlueManifest = """
        {
          "schemaVersion": 1,
          "colors": {
            "windowBackground": "#1940b0",
            "navigationBar": "#122272",
            "progressBar": "#23c99d"
          }
        }
        """;

    [TestMethod]
    public async Task A_host_reads_the_colours_its_theme_names()
    {
        var assets = await CreateUiAssets();

        var colors = await ThemeColors.LoadAsync(assets, "blue", TestCt);

        Assert.AreEqual((VhColor?)VhColor.FromRgb(0x19, 0x40, 0xb0), colors.WindowBackgroundColor);
        Assert.AreEqual((VhColor?)VhColor.FromRgb(0x12, 0x22, 0x72), colors.NavigationBarColor);
        Assert.AreEqual((VhColor?)VhColor.FromRgb(0x23, 0xc9, 0x9d), colors.ProgressBarColor);
    }

    // A theme the files lack is a broken build, which the build refuses (VhReadLaunchColor); a store that slips past
    // it still starts the app, its chrome in the system's colours.
    [TestMethod]
    public async Task A_theme_the_files_lack_leaves_the_system_colours()
    {
        var assets = await CreateUiAssets();

        var colors = await ThemeColors.LoadAsync(assets, "violet", TestCt);

        Assert.IsNull(colors.WindowBackgroundColor);
        Assert.IsNull(colors.NavigationBarColor);
        Assert.IsNull(colors.ProgressBarColor);
    }

    // the UI's files with the blue theme alone
    private async Task<FolderAssetProvider> CreateUiAssets()
    {
        var folder = Path.Combine(TestHelper.WorkingPath, "ui");
        var themeFolder = Path.Combine(folder, "themes", "blue");
        Directory.CreateDirectory(themeFolder);
        await File.WriteAllTextAsync(Path.Combine(themeFolder, "manifest.json"), BlueManifest, TestCt);
        return new FolderAssetProvider(folder);
    }
}

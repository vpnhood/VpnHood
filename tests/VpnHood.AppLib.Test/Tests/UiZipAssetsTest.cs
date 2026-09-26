using System.IO.Compression;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppLib.Test.Tests;

// The UI's store as a head names it (AppOptions.UiZipAssets), with a fork's zip named before it:
// the first zip that holds a file is the one it is read from, so a fork replaces the files it ships -
// its logo - and every other file still comes from the store.
[TestClass]
public class UiZipAssetsTest : TestAppBase
{
    [TestMethod]
    public async Task A_zip_named_before_the_store_replaces_only_its_own_files()
    {
        var store = CreateZip(("images/logo.png", "store-logo"), ("locales/en.json", "store-words"));
        var fork = CreateZip(("images/logo.png", "fork-logo"));

        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.UiZipAssets = [fork, store];
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        var assets = app.UiAssetProvider ?? throw new InvalidOperationException("The app made no UI asset provider.");

        Assert.AreEqual("fork-logo", await ReadText(assets, "images/logo.png"));
        Assert.AreEqual("store-words", await ReadText(assets, "locales/en.json"));
    }

    private IAsset CreateZip(params (string Path, string Text)[] files)
    {
        Directory.CreateDirectory(TestAppHelper.WorkingPath);
        var path = Path.Combine(TestAppHelper.WorkingPath, $"ui_{Guid.CreateVersion7()}.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create)) {
            foreach (var (entryPath, text) in files) {
                using var writer = new StreamWriter(archive.CreateEntry(entryPath).Open());
                writer.Write(text);
            }
        }

        return new FileAsset(path);
    }

    private async Task<string> ReadText(IAssetProvider assets, string assetPath)
    {
        await using var stream = await assets.OpenReadAsync(assetPath, TestContext.CancellationToken);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(TestContext.CancellationToken);
    }
}

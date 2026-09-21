using VpnHood.Core.Toolkit.Assets;

namespace VpnHood.Test.Tests;

[TestClass]
public class AssetProviderTest
{
    private static string CreateFolder(params (string Path, string Text)[] files)
    {
        var folder = Path.Combine(TestHelper.AssemblyWorkingPath, "assets_" + Guid.CreateVersion7());
        foreach (var (path, text) in files) {
            var file = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file) ?? folder);
            File.WriteAllText(file, text);
        }

        return folder;
    }

    private static async Task<string> ReadText(IAssetProvider provider, string assetPath)
    {
        await using var stream = await provider.OpenReadAsync(assetPath, CancellationToken.None);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    [TestMethod]
    public async Task Composite_reads_the_first_provider_that_has_the_asset()
    {
        var overlay = CreateFolder(("images/logo.png", "overlay-logo"));
        var store = CreateFolder(("images/logo.png", "store-logo"), ("images/other.png", "store-other"));
        try {
            var composite = new CompositeAssetProvider([new FolderAssetProvider(overlay), new FolderAssetProvider(store)]);

            // the overlay shadows the store at the same path; what it lacks falls through
            Assert.AreEqual("overlay-logo", await ReadText(composite, "images/logo.png"));
            Assert.AreEqual("store-other", await ReadText(composite, "images/other.png"));

            // absent everywhere: the same answer any single provider gives
            await Assert.ThrowsExactlyAsync<AssetNotFoundException>(() =>
                composite.OpenReadAsync("images/none.png", CancellationToken.None));
            Assert.IsNull(await composite.TryOpenReadAsync("images/none.png", CancellationToken.None));
        }
        finally {
            Directory.Delete(overlay, recursive: true);
            Directory.Delete(store, recursive: true);
        }
    }

    [TestMethod]
    public void Composite_refuses_an_empty_list()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CompositeAssetProvider([]));
    }
}

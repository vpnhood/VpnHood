using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.Test.Tests;

[TestClass]
public class AssetProviderTest
{
    public required TestContext TestContext { get; set; }
    private CancellationToken TestCt => TestContext.CancellationToken;

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

    private async Task<string> ReadText(IAssetProvider provider, string assetPath)
    {
        await using var stream = await provider.OpenReadAsync(assetPath, TestCt);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(TestCt);
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
                composite.OpenReadAsync("images/none.png", TestCt));
            Assert.IsNull(await composite.TryOpenReadAsync("images/none.png", TestCt));
        }
        finally {
            Directory.Delete(overlay, recursive: true);
            Directory.Delete(store, recursive: true);
        }
    }

    // a root's path ends with its separator already: one more made a prefix no file under it has
    [TestMethod]
    public async Task A_folder_that_is_a_root_serves_its_files()
    {
        var folder = CreateFolder(("a.txt", "root-file"));
        try {
            var root = Path.GetPathRoot(folder) ?? throw new InvalidOperationException("The folder has no root.");
            var assetPath = Path.GetRelativePath(root, Path.Combine(folder, "a.txt"))
                .Replace(Path.DirectorySeparatorChar, '/');

            Assert.AreEqual("root-file", await ReadText(new FolderAssetProvider(root), assetPath));
        }
        finally {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task A_file_that_is_not_there_is_a_missing_asset()
    {
        var folder = CreateFolder(("a.txt", "file"));
        try {
            await Assert.ThrowsExactlyAsync<AssetNotFoundException>(() =>
                new FileAsset(Path.Combine(folder, "none.txt")).OpenReadAsync(TestCt));
            await Assert.ThrowsExactlyAsync<AssetNotFoundException>(() =>
                new FileAsset(Path.Combine(folder, "none", "a.txt")).OpenReadAsync(TestCt));

            // a folder is no file, nor is what lies outside the provider's folder
            var provider = new FolderAssetProvider(folder);
            await Assert.ThrowsExactlyAsync<AssetNotFoundException>(() => provider.OpenReadAsync("", TestCt));
            await Assert.ThrowsExactlyAsync<AssetNotFoundException>(() => provider.OpenReadAsync("../a.txt", TestCt));
        }
        finally {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void Composite_refuses_an_empty_list()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CompositeAssetProvider([]));
    }
}

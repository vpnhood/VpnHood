using System.IO.Compression;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.Test.Tests;

[TestClass]
public class ZipAssetProviderTest : TestBase
{
    // a zip's entries are extracted once, then read from the copy as files
    [TestMethod]
    public async Task An_entry_reads_from_the_extracted_copy()
    {
        var folderPath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        var sourcePath = Path.Combine(folderPath, "source");
        Directory.CreateDirectory(Path.Combine(sourcePath, "dir"));
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "dir", "a.txt"), "hello", TestCt);
        var zipPath = Path.Combine(folderPath, "assets.zip");
        await ZipFile.CreateFromDirectoryAsync(sourcePath, zipPath, TestCt);
        var assetProvider = new ZipAssetProvider(new FileAsset(zipPath), Path.Combine(folderPath, "extracted"));

        await using var stream = await assetProvider.OpenReadAsync("dir/a.txt", TestCt);
        using var reader = new StreamReader(stream);
        Assert.AreEqual("hello", await reader.ReadToEndAsync(TestCt));
    }
}

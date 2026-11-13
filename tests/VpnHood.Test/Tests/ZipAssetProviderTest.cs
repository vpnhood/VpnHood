using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
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
        var zipPath = await WriteZip(folderPath, stamped: true, "dir/a.txt", "hello");
        var assetProvider = new ZipAssetProvider(new FileAsset(zipPath), Path.Combine(folderPath, "extracted"));

        Assert.AreEqual("hello", await ReadEntry(assetProvider, "dir/a.txt"));
    }

    // The stamp names the copy, and nothing else of the zip is read to tell it: the same stamp keeps
    // the copy whatever the bytes, and a new one extracts again, leaving no other copy behind.
    [TestMethod]
    public async Task The_stamp_names_the_copy()
    {
        var folderPath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        var extractPath = Path.Combine(folderPath, "extracted");
        var zipPath = await WriteZip(folderPath, stamped: true, "a.txt", "first");
        var zip = new FileAsset(zipPath);
        var firstStamp = await ZipStamp.ReadAsync(zip, TestCt);
        Assert.AreEqual("first", await ReadEntry(new ZipAssetProvider(zip, extractPath), "a.txt"));
        Assert.IsTrue(Directory.Exists(Path.Combine(extractPath, firstStamp)));

        // new bytes under the same stamp: the copy stands
        await WriteZip(folderPath, stamped: true, "a.txt", "second");
        var secondStamp = await ZipStamp.ReadAsync(zip, TestCt);
        await SetStamp(zipPath, firstStamp);
        Assert.AreEqual("first", await ReadEntry(new ZipAssetProvider(zip, extractPath), "a.txt"));

        // a new stamp: extracted again, and the old copy goes
        await SetStamp(zipPath, secondStamp);
        Assert.AreEqual("second", await ReadEntry(new ZipAssetProvider(zip, extractPath), "a.txt"));
        CollectionAssert.AreEqual(new[] { secondStamp },
            Directory.GetDirectories(extractPath).Select(Path.GetFileName).ToArray());
    }

    // The cleanup after an extraction takes old versions and unfinished copies, but not a copy another instance is
    // writing at that moment: deleted under its writer, it would be finished and marked with files missing.
    [TestMethod]
    public async Task A_copy_another_instance_is_writing_is_left_alone_and_a_leftover_goes()
    {
        var folderPath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        var extractPath = Path.Combine(folderPath, "extracted");
        var zipPath = await WriteZip(folderPath, stamped: true, "a.txt", "hello");

        var writingPath = Path.Combine(extractPath, $"0123456789abcdef.{Guid.NewGuid():N}.tmp");
        var leftoverPath = Path.Combine(extractPath, $"0123456789abcdef.{Guid.NewGuid():N}.tmp");
        var oldVersionPath = Path.Combine(extractPath, "fedcba9876543210");
        foreach (var path in new[] { writingPath, leftoverPath, oldVersionPath }) {
            Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(Path.Combine(path, "a.txt"), "other", TestCt);
        }

        // the leftover's writer is long gone
        Directory.SetLastWriteTimeUtc(leftoverPath, DateTime.UtcNow - TimeSpan.FromHours(2));

        Assert.AreEqual("hello", await ReadEntry(new ZipAssetProvider(new FileAsset(zipPath), extractPath), "a.txt"));
        Assert.IsTrue(File.Exists(Path.Combine(writingPath, "a.txt")), "The copy being written was deleted.");
        Assert.IsFalse(Directory.Exists(leftoverPath));
        Assert.IsFalse(Directory.Exists(oldVersionPath));
    }

    // a zip without a stamp, a fork's packed by its own tool, still extracts, named by its hash
    [TestMethod]
    public async Task A_zip_without_a_stamp_still_extracts()
    {
        var folderPath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        var extractPath = Path.Combine(folderPath, "extracted");
        var zipPath = await WriteZip(folderPath, stamped: false, "a.txt", "hello");
        var hash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(zipPath, TestCt)).AsSpan(0, 8));

        Assert.AreEqual("hello", await ReadEntry(new ZipAssetProvider(new FileAsset(zipPath), extractPath), "a.txt"));
        Assert.IsTrue(Directory.Exists(Path.Combine(extractPath, hash)));
    }

    private async Task<string> WriteZip(string folderPath, bool stamped, string entryName, string text)
    {
        Directory.CreateDirectory(folderPath);
        var zipPath = Path.Combine(folderPath, "assets.zip");
        await using var fileStream = File.Create(zipPath);
        await using var archive = stamped
            ? new StampedZipArchive(fileStream)
            : new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: true);
        await using var entryStream = await archive.CreateEntry(entryName).OpenAsync(TestCt);
        await using var writer = new StreamWriter(entryStream);
        await writer.WriteAsync(text);
        return zipPath;
    }

    // the stamp's digits sit right after "VHSTAMP:"
    private async Task SetStamp(string zipPath, string stamp)
    {
        await using var stream = File.OpenWrite(zipPath);
        stream.Position = "VHSTAMP:".Length;
        await stream.WriteAsync(Encoding.ASCII.GetBytes(stamp), TestCt);
    }

    private async Task<string> ReadEntry(ZipAssetProvider assetProvider, string assetPath)
    {
        await using var stream = await assetProvider.OpenReadAsync(assetPath, TestCt);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(TestCt);
    }
}

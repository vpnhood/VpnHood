using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class ZipStampTest : TestBase
{
    // The stamp is the whole file's hash, taken while its digits were zero, and the zip behind it still
    // opens - however the archive was closed.
    [TestMethod]
    public async Task The_stamp_is_the_files_hash_with_its_digits_zero()
    {
        foreach (var closeAsync in new[] { false, true }) {
            var zipBytes = await CreateStampedZip(closeAsync, "a.txt", "hello"u8.ToArray());

            var zeroed = zipBytes.ToArray();
            Array.Clear(zeroed, "VHSTAMP:".Length, 16);
            Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(zeroed).AsSpan(0, 8)),
                await ZipStamp.ReadAsync(new MemoryStream(zipBytes), "test.zip", TestCt));

            await using var archive = new ZipArchive(new MemoryStream(zipBytes));
            Assert.AreEqual("a.txt", archive.Entries.Single().FullName);
        }
    }

    // Only the bytes in front of the zip: the megabyte behind them is never read, as on an Android
    // asset's forward-only stream, which a ZipArchive would copy whole first.
    [TestMethod]
    public async Task The_stamp_is_read_from_the_first_bytes()
    {
        var zipBytes = await CreateStampedZip(closeAsync: true, "big.bin", RandomNumberGenerator.GetBytes(1024 * 1024));

        var stream = new TestForwardOnlyStream(zipBytes);
        Assert.AreEqual(Encoding.ASCII.GetString(zipBytes, "VHSTAMP:".Length, 16),
            await ZipStamp.ReadAsync(stream, "test.zip", TestCt));
        Assert.IsLessThan(100L, stream.BytesRead, "Only the stamp is read.");
    }

    // A zip without a stamp, a fork's packed by its own tool, is read whole and named by its hash.
    [TestMethod]
    public async Task A_zip_without_a_stamp_is_hashed_whole()
    {
        using var plain = new MemoryStream();
        await using (var archive = new ZipArchive(plain, ZipArchiveMode.Create, leaveOpen: true))
            await WriteEntry(archive, "a.txt", "hello"u8.ToArray());

        var zipBytes = plain.ToArray();
        var stream = new TestForwardOnlyStream(zipBytes);
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(zipBytes).AsSpan(0, 8)),
            await ZipStamp.ReadAsync(stream, "test.zip", TestCt));
        Assert.AreEqual(zipBytes.Length, stream.BytesRead);
    }

    private async Task<byte[]> CreateStampedZip(bool closeAsync, string entryName, byte[] data)
    {
        using var stream = new MemoryStream();
        var archive = new StampedZipArchive(stream);
        await WriteEntry(archive, entryName, data);
        if (closeAsync)
            await archive.DisposeAsync();
        else
            archive.Dispose();

        return stream.ToArray();
    }

    private async Task WriteEntry(ZipArchive archive, string name, byte[] data)
    {
        await using var entryStream = await archive.CreateEntry(name, CompressionLevel.NoCompression).OpenAsync(TestCt);
        await entryStream.WriteAsync(data, TestCt);
    }
}

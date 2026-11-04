using VpnHood.Net.Toolkit.Storages;

namespace VpnHood.Test.Tests;

[TestClass]
public class StorageTest
{
    private static string CreateFolderPath() =>
        Path.Combine(TestHelper.AssemblyWorkingPath, "storage_" + Guid.CreateVersion7());

    [TestMethod]
    public void A_storage_takes_a_name_never_a_path()
    {
        var storage = new Storage(new FileSystemStorageProvider(), CreateFolderPath());

        foreach (var name in new[] { "", ".", "..", "../x", "a/b", "a\\b", "C:", "C:x" }) {
            Assert.ThrowsExactly<ArgumentException>(() => storage.OpenStorage(name), $"OpenStorage({name})");
            Assert.ThrowsExactly<ArgumentException>(() => storage.Write(name, "x"), $"Write({name})");
            Assert.ThrowsExactly<ArgumentException>(() => storage.Read(name), $"Read({name})");
        }
    }

    [TestMethod]
    public void A_nested_storage_writes_the_file_the_full_path_named()
    {
        var folderPath = CreateFolderPath();
        var storage = new Storage(new FileSystemStorageProvider(), folderPath);
        var domains = storage.OpenStorage("splits").OpenStorage("domains");

        domains.Write("includes.txt", "example.com");

        var filePath = Path.Combine(folderPath, "splits", "domains", "includes.txt");
        Assert.AreEqual("example.com", File.ReadAllText(filePath));
        Assert.AreEqual(Path.GetDirectoryName(filePath), domains.FolderPath);
        Assert.AreEqual("example.com", storage.OpenStorage("splits").OpenStorage("domains").Read("includes.txt"));
        Assert.IsNull(storage.Read("includes.txt"), "A nested storage's file shows in its parent.");

        domains.Delete("includes.txt");
        Assert.IsFalse(File.Exists(filePath));
        Assert.IsNull(domains.Read("includes.txt"));
    }

    [TestMethod]
    public void A_rewrite_replaces_the_file_whole_and_leaves_nothing_beside_it()
    {
        var folderPath = CreateFolderPath();
        var storage = new Storage(new FileSystemStorageProvider(), folderPath);

        storage.Write("settings.json", "first, the longer one");
        var signature = storage.GetSignature("settings.json");
        storage.Write("settings.json", "second");

        Assert.AreEqual("second", storage.Read("settings.json"));
        Assert.AreNotEqual(signature, storage.GetSignature("settings.json"));
        CollectionAssert.AreEqual(new[] { "settings.json" },
            Directory.GetFiles(folderPath).Select(Path.GetFileName).ToArray(), "A temp file was left beside it.");
    }

    [TestMethod]
    public void A_failed_write_leaves_no_temp_file()
    {
        var folderPath = CreateFolderPath();
        var storage = new Storage(new FileSystemStorageProvider(), folderPath);

        // a folder where the file would go, so the move over it fails
        Directory.CreateDirectory(Path.Combine(folderPath, "taken"));

        Assert.Throws<Exception>(() => storage.Write("taken", "x"));
        Assert.IsEmpty(Directory.GetFiles(folderPath), "A temp file was left beside it.");
    }
}

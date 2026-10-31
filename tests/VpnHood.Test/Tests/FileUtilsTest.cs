using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Test.Tests;

// a file replaced whole: a reader finds the old file or the new one, never a part
[TestClass]
public class FileUtilsTest : TestBase
{
    [TestMethod]
    public async Task An_atomic_write_replaces_the_file_and_leaves_no_temp()
    {
        var filePath = CreateFilePath();
        await File.WriteAllTextAsync(filePath, "old", TestCt);

        await FileUtils.WriteAllTextAtomicAsync(filePath, "new", TestCt);

        Assert.AreEqual("new", await File.ReadAllTextAsync(filePath, TestCt));
        Assert.HasCount(1, Directory.GetFiles(Path.GetDirectoryName(filePath)!));
    }

    [TestMethod]
    public async Task A_cancelled_atomic_write_leaves_the_old_file()
    {
        var filePath = CreateFilePath();
        await File.WriteAllTextAsync(filePath, "old", TestCt);
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            FileUtils.WriteAllTextAtomicAsync(filePath, "new", cancellationTokenSource.Token));

        Assert.AreEqual("old", await File.ReadAllTextAsync(filePath, TestCt));
    }

    [TestMethod]
    public async Task A_failed_replace_leaves_no_temp()
    {
        // a folder in the file's place, which the replace can't take
        var filePath = CreateFilePath();
        Directory.CreateDirectory(filePath);

        await Assert.ThrowsAsync<Exception>(() => FileUtils.WriteAllTextAtomicAsync(filePath, "new", TestCt));

        Assert.IsEmpty(Directory.GetFiles(Path.GetDirectoryName(filePath)!));
    }

    private string CreateFilePath()
    {
        var folderPath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        Directory.CreateDirectory(folderPath);
        return Path.Combine(folderPath, "file.json");
    }
}

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

    [TestMethod]
    public async Task A_missing_file_reads_as_null()
    {
        Assert.IsNull(await FileUtils.TryReadAllTextAsync(CreateFilePath(), TestCt));
    }

    [TestMethod]
    public async Task A_cancelled_read_goes_out_rather_than_reading_as_a_missing_file()
    {
        var filePath = CreateFilePath();
        await File.WriteAllTextAsync(filePath, "old", TestCt);
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            FileUtils.TryReadAllTextAsync(filePath, cancellationTokenSource.Token));
    }

    [TestMethod]
    public async Task A_move_puts_the_file_in_its_new_place_or_discards_it_for_the_one_there()
    {
        var sourcePath = CreateFilePath();
        var targetPath = CreateFilePath();
        await File.WriteAllTextAsync(sourcePath, "new", TestCt);

        Assert.IsTrue(FileUtils.MoveOrDiscard(sourcePath, targetPath));
        Assert.IsFalse(File.Exists(sourcePath));
        Assert.AreEqual("new", await File.ReadAllTextAsync(targetPath, TestCt));

        // the new place has one already, which is the one in use
        await File.WriteAllTextAsync(sourcePath, "newer", TestCt);
        Assert.IsTrue(FileUtils.MoveOrDiscard(sourcePath, targetPath));
        Assert.IsFalse(File.Exists(sourcePath));
        Assert.AreEqual("new", await File.ReadAllTextAsync(targetPath, TestCt));

        // gone already
        Assert.IsFalse(FileUtils.MoveOrDiscard(sourcePath, targetPath));
    }

    [TestMethod]
    public async Task A_move_to_its_own_place_keeps_the_file_and_the_folder()
    {
        var filePath = CreateFilePath();
        await File.WriteAllTextAsync(filePath, "only copy", TestCt);

        Assert.IsTrue(FileUtils.MoveOrDiscard(filePath, filePath));
        Assert.AreEqual("only copy", await File.ReadAllTextAsync(filePath, TestCt));

        // a folder, named with and without its last separator
        var folderPath = Path.GetDirectoryName(filePath)!;
        Assert.IsTrue(FileUtils.MoveOrDiscard(folderPath, folderPath + Path.DirectorySeparatorChar));
        Assert.AreEqual("only copy", await File.ReadAllTextAsync(filePath, TestCt));
    }

    [TestMethod]
    public async Task A_move_into_a_missing_folder_fails_and_keeps_the_file()
    {
        var sourcePath = CreateFilePath();
        await File.WriteAllTextAsync(sourcePath, "kept", TestCt);
        var targetPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, "missing", "file.json");

        // not "gone": the file is there, and its new place is what is missing
        Assert.ThrowsExactly<DirectoryNotFoundException>(() => FileUtils.MoveOrDiscard(sourcePath, targetPath));
        Assert.AreEqual("kept", await File.ReadAllTextAsync(sourcePath, TestCt));
    }

    private string CreateFilePath()
    {
        var folderPath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        Directory.CreateDirectory(folderPath);
        return Path.Combine(folderPath, "file.json");
    }
}

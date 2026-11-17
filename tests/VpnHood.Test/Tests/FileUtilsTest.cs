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

    // A link gives one place two names, as Android's /data/data and iOS's /var do. Compared as they are written, the
    // two were a place and a new place that has one already, so the only copy was deleted
    [TestMethod]
    public async Task A_move_to_its_own_place_through_a_link_keeps_the_only_copy()
    {
        var filePath = CreateFilePath();
        await File.WriteAllTextAsync(filePath, "only copy", TestCt);
        var folderPath = Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("No folder.");
        var linkPath = folderPath + "-link";
        await CreateFolderLink(linkPath, folderPath);

        // the file, by the link in its parent folder
        Assert.IsTrue(FileUtils.MoveOrDiscard(filePath, Path.Combine(linkPath, "file.json")));
        Assert.AreEqual("only copy", await File.ReadAllTextAsync(filePath, TestCt));

        // the folder and its link, either way round
        Assert.IsTrue(FileUtils.MoveOrDiscard(folderPath, linkPath));
        Assert.IsTrue(FileUtils.MoveOrDiscard(linkPath, folderPath));
        Assert.AreEqual("only copy", await File.ReadAllTextAsync(filePath, TestCt));
    }

    // Deleted for the one inside it, which is the one in use, the folder took that one along
    [TestMethod]
    public async Task A_move_of_a_folder_into_itself_is_refused_and_keeps_it()
    {
        var filePath = CreateFilePath();
        await File.WriteAllTextAsync(filePath, "kept", TestCt);
        var folderPath = Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("No folder.");
        var innerFolderPath = Path.Combine(folderPath, "inner");
        Directory.CreateDirectory(innerFolderPath);
        var innerFilePath = Path.Combine(innerFolderPath, "file.json");
        await File.WriteAllTextAsync(innerFilePath, "kept too", TestCt);

        // a place inside that has one already, and one that has none
        Assert.ThrowsExactly<IOException>(() => FileUtils.MoveOrDiscard(folderPath, innerFolderPath));
        Assert.ThrowsExactly<IOException>(() => FileUtils.MoveOrDiscard(folderPath, Path.Combine(folderPath, "new")));

        // a link inside that leads out of the folder: what it leads to would stay, and the link go with the folder
        var outerFolderPath = folderPath + "-outer";
        Directory.CreateDirectory(outerFolderPath);
        var linkPath = Path.Combine(folderPath, "link");
        await CreateFolderLink(linkPath, outerFolderPath);
        Assert.ThrowsExactly<IOException>(() => FileUtils.MoveOrDiscard(folderPath, linkPath));
        Assert.IsTrue(Directory.Exists(linkPath));

        Assert.AreEqual("kept", await File.ReadAllTextAsync(filePath, TestCt));
        Assert.AreEqual("kept too", await File.ReadAllTextAsync(innerFilePath, TestCt));
    }

    // Windows names one place with and without its device prefix
    [TestMethod]
    public async Task A_move_to_its_own_place_by_its_device_path_keeps_the_only_copy()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Only Windows has device paths.");

        var filePath = CreateFilePath();
        await File.WriteAllTextAsync(filePath, "only copy", TestCt);

        Assert.IsTrue(FileUtils.MoveOrDiscard(filePath, @"\\?\" + filePath));
        Assert.IsTrue(FileUtils.MoveOrDiscard(@"\\?\" + filePath, filePath));
        Assert.AreEqual("only copy", await File.ReadAllTextAsync(filePath, TestCt));
    }

    // A link's target with ".." after a link: unix leaves the folder that link leads to, and Windows the name as it
    // is written, so the two reach different files. The move must take the link for the file the system reaches
    [TestMethod]
    public async Task A_link_is_followed_as_the_system_follows_it()
    {
        // folder/inner/file.json and folder/file.json, and folder/side, a link to folder/inner/deep
        var filePath = CreateFilePath();
        await File.WriteAllTextAsync(filePath, "outer", TestCt);
        var folderPath = Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("No folder.");
        var innerFilePath = Path.Combine(folderPath, "inner", "file.json");
        Directory.CreateDirectory(Path.Combine(folderPath, "inner", "deep"));
        await File.WriteAllTextAsync(innerFilePath, "inner", TestCt);
        await CreateFolderLink(Path.Combine(folderPath, "side"), Path.Combine(folderPath, "inner", "deep"));

        // folder/link, which leads to side/../file.json
        var linkPath = Path.Combine(folderPath, "link.json");
        try {
            File.CreateSymbolicLink(linkPath, Path.Combine("side", "..", "file.json"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            Assert.Inconclusive($"This account may not make a symbolic link. {ex.Message}");
        }

        // the file the system reaches by the link is the link's own place, and stays; the other is the old one
        var isInnerReached = await File.ReadAllTextAsync(linkPath, TestCt) == "inner";
        var reachedFilePath = isInnerReached ? innerFilePath : filePath;
        var otherFilePath = isInnerReached ? filePath : innerFilePath;
        Assert.IsTrue(FileUtils.MoveOrDiscard(reachedFilePath, linkPath));
        Assert.IsTrue(File.Exists(reachedFilePath));
        Assert.IsTrue(FileUtils.MoveOrDiscard(otherFilePath, linkPath));
        Assert.IsFalse(File.Exists(otherFilePath));
        Assert.IsTrue(File.Exists(reachedFilePath));
    }

    // a symbolic link, or on Windows a junction, which needs no right a test may lack
    private Task CreateFolderLink(string linkPath, string folderPath)
    {
        if (OperatingSystem.IsWindows())
            return OsUtils.ExecuteCommandAsync("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{folderPath}\"", TestCt);

        Directory.CreateSymbolicLink(linkPath, folderPath);
        return Task.CompletedTask;
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

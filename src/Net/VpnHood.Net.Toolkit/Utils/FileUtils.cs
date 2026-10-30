using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Toolkit.Utils;

public static class FileUtils
{
    // A file or folder moved to its new place - a rename on one volume - unless the new place already has
    // one, which is then the one in use, so the old goes. False where the old is gone: another process
    // may have moved it first.
    public static bool MoveOrDiscard(string sourcePath, string targetPath)
    {
        try {
            var isFolder = Directory.Exists(sourcePath);
            if (!isFolder && !File.Exists(sourcePath))
                return false;

            var isTargetPresent = File.Exists(targetPath) || Directory.Exists(targetPath);
            if (isFolder && isTargetPresent) Directory.Delete(sourcePath, recursive: true);
            else if (isFolder) Directory.Move(sourcePath, targetPath);
            else if (isTargetPresent) File.Delete(sourcePath);
            else File.Move(sourcePath, targetPath);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) {
            return false; // another process moved it in the meantime
        }
    }

    // A file two processes share - one writes it while the other may be reading it - fails the access that meets
    // the other's for that moment only, so the access is tried again up to the timeout; a missing file fails
    // at once. The token ends the wait between tries.
    public static async Task<T> WaitOutSharingAsync<T>(Func<Task<T>> access, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        // don't use fast date time, it's not accurate enough
        var startTime = DateTime.UtcNow;
        while (true) {
            try {
                return await access().Vhc();
            }
            catch (Exception ex) when (IsSharingConflict(ex) && DateTime.UtcNow - startTime < timeout) {
                await Task.Delay(50, cancellationToken).Vhc();
            }
        }
    }

    public static Task WaitOutSharingAsync(Func<Task> access, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return WaitOutSharingAsync(async () => {
            await access().Vhc();
            return true;
        }, timeout, cancellationToken);
    }

    // an open that meets the other's sharing, or a replace over a file the other has open, which Windows refuses
    // as access denied whatever its sharing; not a missing file
    private static bool IsSharingConflict(Exception ex)
    {
        return ex is UnauthorizedAccessException ||
               ex is IOException and not FileNotFoundException and not DirectoryNotFoundException;
    }

    public static async Task WriteAllTextRetryAsync(string filePath, string content, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        // don't use fast date time, it's not accurate enough
        var startTime = DateTime.UtcNow;
        while (true) {
            try {
                await File.WriteAllTextAsync(filePath, content, cancellationToken).Vhc();
                return;
            }
            catch (IOException) when (DateTime.UtcNow - startTime < timeout) {
                await Task.Delay(100, cancellationToken).Vhc();
            }
        }
    }

    public static async Task<string> ReadAllTextAsync(string filePath, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        // don't use fast date time, it's not accurate enough
        var startTime = DateTime.UtcNow;
        while (true) {
            try {
                await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync(cancellationToken).Vhc();
                return content;
            }
            catch (IOException) when (DateTime.UtcNow - startTime < timeout) {
                await Task.Delay(100, cancellationToken).Vhc();
            }
        }
    }

    // a name from outside, so a file of it stays in its folder: "..\x" becomes ".._x"
    public static string ReplaceInvalidFileNameChars(string name)
    {
        return string.Join('_', name.Split(Path.GetInvalidFileNameChars()));
    }
}
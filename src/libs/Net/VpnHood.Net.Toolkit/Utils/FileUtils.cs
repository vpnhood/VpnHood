using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Net.Toolkit.Utils;

/// <summary>
/// Provides file helpers: moves, atomic writes, and reads and writes that wait out another process's sharing.
/// </summary>
public static class FileUtils
{
    // the links one path may pass before it counts as a loop, as Linux has it
    private const int MaxLinkCount = 40;

    /// <summary>
    /// Moves a file or a folder to a new place, unless the new place has one already, which is then the one in use, so the
    /// old one is deleted.
    /// </summary>
    /// <param name="sourcePath">The file's or folder's path.</param>
    /// <param name="targetPath">The new path.</param>
    /// <returns>
    /// <see langword="true"/> if it was moved or deleted, or the two paths are the same place, by their names or
    /// through a link; <see langword="false"/> if it is gone, as another process may have moved it first.
    /// </returns>
    /// <exception cref="IOException">
    /// The move or the deletion failed, as when the new place's folder is missing; the new place lies inside the
    /// folder to move; or a path passes more than 40 links.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">The process may not move or delete it.</exception>
    public static bool MoveOrDiscard(string sourcePath, string targetPath)
    {
        try {
            var isFolder = Directory.Exists(sourcePath);
            if (!isFolder && !File.Exists(sourcePath))
                return false;

            // The same place: it is there already. Taken for a new place that has one, it would be deleted, the
            // only copy with it. By where the paths lead, as a link gives one place two names: Android's
            // /data/data, iOS's /var.
            var source = ResolveLinks(sourcePath, followLast: true);
            var target = ResolveLinks(targetPath, followLast: true);
            if (IsSamePath(source, target))
                return true;

            // Deleted for the one in it, the folder would take that one along; moved, it cannot go into itself. By
            // where the new place leads, and by where its own name lies: a link in the folder that leads out of it
            // would go with the folder too
            if (isFolder &&
                (IsInside(target, source) || IsInside(ResolveLinks(targetPath, followLast: false), source)))
                throw new IOException(
                    $"The new place lies inside the folder to move. SourcePath: {sourcePath}, TargetPath: {targetPath}");

            var isTargetPresent = File.Exists(targetPath) || Directory.Exists(targetPath);
            if (isFolder && isTargetPresent) Directory.Delete(sourcePath, recursive: true);
            else if (isFolder) Directory.Move(sourcePath, targetPath);
            else if (isTargetPresent) File.Delete(sourcePath);
            else File.Move(sourcePath, targetPath);
            return true;
        }
        // another process moved it in the meantime; with the source still there, the new place is what is missing
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException &&
                                   !File.Exists(sourcePath) && !Directory.Exists(sourcePath)) {
            return false;
        }
    }

    // as the file system compares names: without their case on Windows and macOS
    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    // of two paths as ResolveLinks gives them
    private static bool IsSamePath(string path1, string path2)
    {
        return string.Equals(path1, path2, PathComparison);
    }

    // of two paths as ResolveLinks gives them
    private static bool IsInside(string path, string folderPath)
    {
        return path.StartsWith(Path.TrimEndingDirectorySeparator(folderPath) + Path.DirectorySeparatorChar,
            PathComparison);
    }

    // The path as the file system reaches it: full, without its last separator, and with the links on its way
    // followed, name by name, as the system follows them. followLast is false for the place the last name itself
    // lies at, whatever it leads to.
    private static string ResolveLinks(string path, bool followLast)
    {
        var fullPath = Path.GetFullPath(TrimDevicePrefix(path));
        var resolvedPath = Path.GetPathRoot(fullPath) ?? string.Empty;
        var names = new Stack<string>();
        PushNames(names, fullPath[resolvedPath.Length..]);

        var linkCount = 0;
        while (names.TryPop(out var name)) {
            if (name == ".")
                continue;

            // the root's parent is the root
            if (name == "..") {
                resolvedPath = Path.GetDirectoryName(resolvedPath) ?? resolvedPath;
                continue;
            }

            var currentPath = Path.Combine(resolvedPath, name);
            var linkTarget = followLast || names.Count > 0 ? GetLinkTarget(currentPath) : null;
            if (linkTarget == null) {
                resolvedPath = currentPath;
                continue;
            }

            if (++linkCount > MaxLinkCount)
                throw new IOException($"The path passes too many links. Path: {path}");

            // Windows takes the ".." of a link's target off the names before them, as they are written, so the
            // target is made full that way, from the folder the link lies in; unix walks them folder by folder,
            // as the loop does
            if (OperatingSystem.IsWindows())
                linkTarget = Path.GetFullPath(TrimDevicePrefix(linkTarget), resolvedPath);

            // a target with a root starts the walk again there; any other goes on from the folder the link lies in
            if (Path.IsPathRooted(linkTarget)) {
                resolvedPath = Path.GetPathRoot(linkTarget) ?? string.Empty;
                linkTarget = linkTarget[resolvedPath.Length..];
            }

            PushNames(names, linkTarget);
        }

        return resolvedPath;
    }

    // a path's names onto the stack, its first name on top
    private static void PushNames(Stack<string> names, string path)
    {
        var pathNames = path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        for (var i = pathNames.Length - 1; i >= 0; i--)
            names.Push(pathNames[i]);
    }

    // What the link at a path is written to lead to; null for what is no link, is not there, or may not be looked
    // at, as a parent folder the system keeps from the app. Such a name counts as it is written, as every name did
    // before links were followed: the walk must not fail a move that needs no look at its parents.
    private static string? GetLinkTarget(string path)
    {
        try {
            var attributes = File.GetAttributes(path);
            if (!attributes.HasFlag(FileAttributes.ReparsePoint))
                return null;

            FileSystemInfo fileSystemInfo = attributes.HasFlag(FileAttributes.Directory)
                ? new DirectoryInfo(path)
                : new FileInfo(path);

            return fileSystemInfo.LinkTarget;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return null;
        }
    }

    // A Windows path written for the device namespace, \\?\C:\folder or \\?\UNC\server\share, as the plain path of
    // the same place: as they are written, the two compare as two places. Any other path as it is; a volume's or a
    // device's own name has no plain form.
    private static string TrimDevicePrefix(string path)
    {
        if (!OperatingSystem.IsWindows() || path.Length < 4 || path[0] != '\\' || path[1] != '\\' ||
            path[2] is not ('?' or '.') || path[3] != '\\')
            return path;

        var devicePath = path[4..];
        if (devicePath.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + devicePath[4..];

        return devicePath.Length >= 2 && char.IsAsciiLetter(devicePath[0]) && devicePath[1] == ':' ? devicePath : path;
    }

    /// <summary>
    /// Runs a file access, and again after 50 milliseconds when it is refused, or fails with an I/O error other than a
    /// missing file, while the time since the first try is under the timeout; a try under way is not cut, so the whole may
    /// last longer. The token cancels the waits between tries, not the access.
    /// </summary>
    /// <typeparam name="T">The type of the access's result.</typeparam>
    /// <param name="access">The access.</param>
    /// <param name="timeout">How long to keep trying.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the access's.</returns>
    /// <exception cref="IOException">The access still meets the sharing at the timeout, or fails otherwise.</exception>
    /// <exception cref="UnauthorizedAccessException">The access is refused, at the timeout.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
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

    /// <summary>
    /// Runs a file access, and again after 50 milliseconds when it is refused, or fails with an I/O error other than a
    /// missing file, while the time since the first try is under the timeout. The token cancels the waits, not the access.
    /// </summary>
    /// <param name="access">The access.</param>
    /// <param name="timeout">How long to keep trying.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="IOException">The access still meets the sharing at the timeout, or fails otherwise.</exception>
    /// <exception cref="UnauthorizedAccessException">The access is refused, at the timeout.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
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

    /// <summary>
    /// Reads a file's text, or gives <see langword="null"/>: a missing file, as on a first run, goes unlogged, and any other
    /// failure, a file gone after the check included, is logged as a warning.
    /// </summary>
    /// <param name="filePath">The file's path.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the text, or <see langword="null"/>.</returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled; taken for a missing file, it would have the caller make the file
    /// anew.
    /// </exception>
    public static async Task<string?> TryReadAllTextAsync(string filePath, CancellationToken cancellationToken)
    {
        try {
            return File.Exists(filePath) ? await File.ReadAllTextAsync(filePath, cancellationToken).Vhc() : null;
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not read the file. FilePath: {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Makes a folder only its owner may use, on Unix: one already there, as another tool may have left it, gets that
    /// mode too, and a link there is refused, as it would lead elsewhere.
    /// </summary>
    /// <param name="path">The folder's path.</param>
    /// <exception cref="IOException">The path is a link, or the folder could not be made.</exception>
    /// <exception cref="UnauthorizedAccessException">The process may not make the folder or set its mode.</exception>
    [UnsupportedOSPlatform("windows")]
    public static void CreateOwnerOnlyDirectory(string path)
    {
        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        var directory = Directory.CreateDirectory(path, ownerOnly);
        if (directory.LinkTarget != null)
            throw new IOException($"The folder is a link, which may lead elsewhere. Path: {path}");

        File.SetUnixFileMode(path, ownerOnly);
    }

    /// <summary>
    /// Replaces a file's text, in UTF-8: the text goes to a file beside it, which is then moved over the file, so a write
    /// that fails or is canceled leaves the old file, not a part of the new one. The move is not canceled, and nothing is
    /// flushed to the disk first.
    /// </summary>
    /// <param name="filePath">The file's path.</param>
    /// <param name="content">The text.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="IOException">The write or the move failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The process may not write the file.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static Task WriteAllTextAtomicAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        return WriteAllBytesAtomicAsync(filePath, Encoding.UTF8.GetBytes(content), cancellationToken);
    }

    /// <summary>Replaces a file's bytes through a file beside it, as <see cref="WriteAllTextAtomicAsync"/> does.</summary>
    /// <param name="filePath">The file's path.</param>
    /// <param name="bytes">The bytes.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="IOException">The write or the move failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The process may not write the file.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task WriteAllBytesAtomicAsync(string filePath, byte[] bytes,
        CancellationToken cancellationToken)
    {
        var tempFilePath = CreateAtomicTempFilePath(filePath);
        try {
            await File.WriteAllBytesAsync(tempFilePath, bytes, cancellationToken).Vhc();
            File.Move(tempFilePath, filePath, overwrite: true);
        }
        catch {
            VhUtils.TryDeleteFile(tempFilePath);
            throw;
        }
    }

    /// <summary>
    /// Replaces a file's text through a file beside it, synchronously, for a write behind a setter: a write that fails,
    /// as on a full disk, leaves the old file.
    /// </summary>
    /// <param name="filePath">The file's path.</param>
    /// <param name="content">The text.</param>
    /// <exception cref="IOException">The write or the move failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The process may not write the file.</exception>
    public static void WriteAllTextAtomic(string filePath, string content)
    {
        var tempFilePath = CreateAtomicTempFilePath(filePath);
        try {
            File.WriteAllText(tempFilePath, content);
            File.Move(tempFilePath, filePath, overwrite: true);
        }
        catch {
            VhUtils.TryDeleteFile(tempFilePath);
            throw;
        }
    }

    // beside the file, so the move is a rename on one volume; a name of its own, so two writes never share it
    private static string CreateAtomicTempFilePath(string filePath)
    {
        return $"{filePath}.{Guid.NewGuid():N}.tmp";
    }

    /// <summary>
    /// Writes a file's text, and again after 100 milliseconds when it fails with an I/O error, while the time since the
    /// first try is under the timeout.
    /// </summary>
    /// <param name="filePath">The file's path.</param>
    /// <param name="content">The text.</param>
    /// <param name="timeout">How long to keep trying.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="IOException">The write still fails at the timeout.</exception>
    /// <exception cref="UnauthorizedAccessException">The process may not write the file.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
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

    /// <summary>
    /// Reads a file's text, sharing it with readers, and again after 100 milliseconds when it fails with an I/O error,
    /// while the time since the first try is under the timeout.
    /// </summary>
    /// <param name="filePath">The file's path.</param>
    /// <param name="timeout">How long to keep trying.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the text.</returns>
    /// <exception cref="IOException">The read still fails at the timeout, or the file is missing then.</exception>
    /// <exception cref="UnauthorizedAccessException">The process may not read the file.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
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

    /// <summary>
    /// Replaces each character a file name may not hold with <c>_</c>, such as a path separator; it leaves <c>..</c> and
    /// reserved names as they are.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The name with those characters replaced.</returns>
    public static string ReplaceInvalidFileNameChars(string name)
    {
        return string.Join('_', name.Split(Path.GetInvalidFileNameChars()));
    }
}
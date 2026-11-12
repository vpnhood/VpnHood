using VpnHood.Core.Common.Exceptions;

namespace VpnHood.AppLib.App;

/// <summary>
/// Provides an app's single-instance lock as a file, in a folder its host names: one only root may write, as the
/// Linux service's, or one the person owns.
/// </summary>
/// <remarks>
/// The file is held open with no sharing, which .NET makes an exclusive <c>flock</c> on Linux, and the system lets it
/// go with the process, so a crash leaves no stale lock. On Linux a new lock file is readable and writable by its
/// owner only, as a lock anyone may read is one anyone may hold; a file that is there keeps its permissions.
/// </remarks>
public static class InstanceLockFile
{
    /// <summary>The lock file's name in its folder.</summary>
    public const string FileName = "instance.lock";

    /// <summary>Takes the lock, making its folder where there is none.</summary>
    /// <param name="folderPath">The folder of the lock file.</param>
    /// <param name="appId">The app's ID, which the exception names.</param>
    /// <returns>The open lock file, which holds the lock until it is disposed or the process ends.</returns>
    /// <exception cref="AnotherInstanceIsRunningException">Another process holds the lock.</exception>
    /// <exception cref="IOException">The file could not be opened for another reason.</exception>
    /// <exception cref="UnauthorizedAccessException">The process may not write the folder or the file.</exception>
    public static FileStream Take(string folderPath, string appId)
    {
        Directory.CreateDirectory(folderPath);
        var options = new FileStreamOptions {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None
        };

        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        try {
            return new FileStream(Path.Combine(folderPath, FileName), options);
        }
        catch (IOException ex) when (IsHeld(ex)) {
            throw new AnotherInstanceIsRunningException($"Another {appId} instance is already running.", ex);
        }
    }

    /// <summary>
    /// Returns a value indicating whether an exception from opening a file says another open holds it: a sharing or
    /// lock violation on Windows, <c>flock</c>'s <c>EWOULDBLOCK</c> on Linux.
    /// </summary>
    /// <param name="ex">The exception from the open.</param>
    /// <returns><see langword="true"/> if another open holds the file; otherwise, <see langword="false"/>.</returns>
    public static bool IsHeld(IOException ex)
    {
        return OperatingSystem.IsWindows()
            ? (ex.HResult & 0xFFFF) is 32 or 33
            : ex.HResult == 11;
    }
}

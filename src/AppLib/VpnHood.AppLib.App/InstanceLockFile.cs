using VpnHood.Core.Common.Exceptions;

namespace VpnHood.AppLib.App;

// An app's single-instance lock as a file, in a folder its host names: one only root may write (the
// Linux service's), or one the person owns (a debugger's run). Held open with no sharing, which .NET
// makes an exclusive flock on Linux, and which the system lets go with the process, so a crash leaves
// no stale lock. Made 0600 on Linux: flock takes a file opened only to read, so a lock anyone may read
// is one anyone may hold.
public static class InstanceLockFile
{
    public const string FileName = "instance.lock";

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

    // Held by another open: a sharing or lock violation on Windows, flock's EWOULDBLOCK on Linux.
    private static bool IsHeld(IOException ex)
    {
        return OperatingSystem.IsWindows()
            ? (ex.HResult & 0xFFFF) is 32 or 33
            : ex.HResult == 11;
    }
}

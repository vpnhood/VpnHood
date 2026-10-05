using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace VpnHood.AppUi.Hosting.Desktop.Windows;

// Whether a folder may hold what the service runs. The service runs as SYSTEM, so a copy where
// others may write - a folder made under C:\ - would let anyone swap a dll and run as SYSTEM
// (desktop plan §3). Only SYSTEM, Administrators and TrustedInstaller may change the folder or
// anything in it, or delete, rename or rewrite a folder on the way to it; adding a folder beside
// one stays allowed, as C:\ lets everyone do. The service is registered under the path it was
// started from, so that path must be the folder's own: reached through a link, a mapped drive or
// another name, it is refused, since whoever made the link may point it elsewhere. All of it is
// read from the access lists alone, which needs no elevation.
internal static class WindowsInstallFolder
{
    // in the folder and below: write or add anything, its attributes included, delete it, or change
    // its access list or owner
    private const FileSystemRights ChangeRights =
        FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteAttributes |
        FileSystemRights.WriteExtendedAttributes | FileSystemRights.DeleteSubdirectoriesAndFiles |
        FileSystemRights.Delete | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    // on the way to it: delete or rename a folder or what it holds, or change its access list or owner
    private const FileSystemRights WayRights =
        FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.Delete |
        FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    // GENERIC_ALL and GENERIC_WRITE, which an entry may hold as they are rather than mapped
    private const int GenericRights = 0x10000000 | 0x40000000;

    private const uint FileReadAttributes = 0x80;
    private const uint FileFlagBackupSemantics = 0x02000000;

    private static readonly SecurityIdentifier TrustedInstaller =
        new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");

    // OWNER RIGHTS: what an entry gives an object's owner, whom the owner's own check covers
    private static readonly SecurityIdentifier OwnerRights = new("S-1-3-4");

    // Why the folder may not hold the service, as a person reads it; null when it may. What cannot
    // be read cannot be vouched for, and is a reason too.
    public static string? FindProblem(string folderPath)
    {
        try {
            return Inspect(folderPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return $"{folderPath} could not be read: {ex.Message.TrimEnd('.')}";
        }
    }

    private static string? Inspect(string folderPath)
    {
        var givenPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
        var folder = GetRealPath(givenPath);
        if (folder.StartsWith(@"\\", StringComparison.Ordinal))
            return $"{folder} is on the network";

        // Each folder on the way is asked itself: in a folder that tells case apart, a link may lead
        // to a name that differs only in case, which the comparison below takes for the same.
        if (FindLinkOnTheWay(givenPath) is { } link)
            return $"{link} is a link; start it from {folder}";

        if (!folder.Equals(givenPath, StringComparison.OrdinalIgnoreCase))
            return $"{givenPath} is not where the folder really is, which is {folder}; start it from there";

        // the folder, and everything in it
        foreach (var path in EnumerateTree(folder)) {
            var isDirectory = File.GetAttributes(path).HasFlag(FileAttributes.Directory);
            if (path != folder && IsLink(path, isDirectory))
                return $"{path} is a link";

            FileSystemSecurity security = isDirectory
                ? new DirectoryInfo(path).GetAccessControl()
                : new FileInfo(path).GetAccessControl();
            if (FindChanger(security, ChangeRights) is { } changer)
                return $"{changer} may change {path}";
        }

        // the folders on the way to it, up to the drive
        for (var parent = Path.GetDirectoryName(folder); parent != null; parent = Path.GetDirectoryName(parent)) {
            if (FindChanger(new DirectoryInfo(parent).GetAccessControl(), WayRights) is { } changer)
                return $"{changer} may rename or replace {parent}";
        }

        return null;
    }

    private static IEnumerable<string> EnumerateTree(string folder)
    {
        // nothing skipped: a hidden file loads as well as any, and one this caller may not read fails the check
        var options = new EnumerationOptions {
            RecurseSubdirectories = true,
            AttributesToSkip = 0,
            IgnoreInaccessible = false
        };
        return Directory.EnumerateFileSystemEntries(folder, "*", options).Prepend(folder);
    }

    // The first folder on the path, from the folder itself up to the drive, that is a link.
    private static string? FindLinkOnTheWay(string path)
    {
        for (var folder = path; folder != null; folder = Path.GetDirectoryName(folder)) {
            if (IsLink(folder, isDirectory: true))
                return folder;
        }

        return null;
    }

    // A symbolic link or a junction. Any other reparse point - a compressed file's, a cloud
    // placeholder's - is the file itself.
    private static bool IsLink(string path, bool isDirectory)
    {
        FileSystemInfo entry = isDirectory ? new DirectoryInfo(path) : new FileInfo(path);
        return entry.LinkTarget != null;
    }

    // Who but SYSTEM, Administrators and TrustedInstaller may do any of what the rights name, the
    // owner included, who may always rewrite the access list. An entry that only passes down to what
    // a folder holds (inherit-only) is not the folder's own: C:\ gives Authenticated Users Modify so,
    // and Program Files gives CREATOR OWNER full control.
    private static string? FindChanger(FileSystemSecurity security, FileSystemRights rights)
    {
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner)
            return "an unknown owner";

        if (!IsTrusted(owner))
            return NameOf(owner);

        foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true,
                     typeof(SecurityIdentifier))) {
            if (rule.AccessControlType != AccessControlType.Allow ||
                rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly) ||
                rule.IdentityReference is not SecurityIdentifier sid ||
                IsTrusted(sid))
                continue;

            if ((rule.FileSystemRights & rights) != 0 || ((int)rule.FileSystemRights & GenericRights) != 0)
                return NameOf(sid);
        }

        return null;
    }

    // CREATOR OWNER stands in for whoever makes an object and is nobody itself; OWNER RIGHTS is the
    // owner, whom the owner's own check covers.
    private static bool IsTrusted(SecurityIdentifier sid)
    {
        return sid.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
               sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) ||
               sid == TrustedInstaller ||
               sid.IsWellKnown(WellKnownSidType.CreatorOwnerSid) ||
               sid == OwnerRights;
    }

    private static string NameOf(SecurityIdentifier sid)
    {
        try {
            return sid.Translate(typeof(NTAccount)).Value;
        }
        catch (IdentityNotMappedException) {
            return sid.Value;
        }
    }

    // Where the folder really is, every link on the way followed: \\?\C:\... for a drive,
    // \\?\UNC\server\share\... for the network.
    private static string GetRealPath(string path)
    {
        using var handle = CreateFile(path, FileReadAttributes, FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero, FileMode.Open, FileFlagBackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new IOException($"Could not open {path}: {new Win32Exception().Message}");

        var buffer = new StringBuilder(1024);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity)
            throw new IOException($"Could not tell where {path} is: {new Win32Exception().Message}");

        var finalPath = buffer.ToString();
        return finalPath.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) ? @"\\" + finalPath[8..]
            : finalPath.StartsWith(@"\\?\", StringComparison.Ordinal) ? finalPath[4..]
            : finalPath;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, FileShare shareMode,
        IntPtr securityAttributes, FileMode creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint pathLength,
        uint flags);
}

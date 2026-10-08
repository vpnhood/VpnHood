using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace VpnHood.Net.Toolkit.Utils;

// Installs files, such as a vendor's DLL and driver, into a folder of their own under Common Files:
// only administrators write there, it is apart from every app folder (a driver holds its file until a
// reboot, which there blocks no update or cleanup), and it is named by the files' hash, so another
// build gets another folder and a file in use is never overwritten.
[SupportedOSPlatform("windows")]
public static class WindowsCommonFiles
{
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);
    private static readonly SecurityIdentifier[] TrustedSids = [
        LocalSystem, Administrators,
        new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"), // TrustedInstaller
        new(WellKnownSidType.CreatorOwnerSid, null), // stands for the owner, trusted itself
        new("S-1-3-4") // OWNER RIGHTS, likewise
    ];

    // write, add, delete or replace, change the permissions, take the ownership; GENERIC_ALL, GENERIC_WRITE
    private const int UnsafeRights = (int)(FileSystemRights.WriteData | FileSystemRights.AppendData |
                                           FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.Delete |
                                           FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership) |
                                     0x10000000 | 0x40000000;

    // Returns the folder holding the files, named by name, such as WinTun-x64, and by the files' hash.
    // Each stream must seek: it is read from its start for its hash, and again to write a missing file.
    // A folder or a file others than administrators may change fails it, never repaired.
    public static string Install(string name, IReadOnlyDictionary<string, Stream> files)
    {
        // what such files serve needs administrators anyway; checked first, for one clear error
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException($"{name} needs administrator rights.");

        var commonFiles = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles));
        if (!IsProtected(commonFiles))
            throw new UnauthorizedAccessException(
                $"{commonFiles.FullName} is a link, or not only administrators may change it, so {name} is not installed there.");

        // each file's SHA-256 names the folder, and then checks the file installed there
        var fileHashes = files.ToDictionary(x => x.Key, x => GetSha256(x.Value));

        // an existing folder is kept as it is, and checked
        var folder = new DirectoryInfo(Path.Combine(commonFiles.FullName, $"{name}-{GetFolderHash(fileHashes)}"));
        folder.Create(CreateSecurity(new DirectorySecurity(),
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit));
        folder.Refresh();
        if (!IsProtected(folder))
            throw new UnauthorizedAccessException(
                $"{folder.FullName} is a link, or not only administrators may change it. Delete it to have it made again.");

        foreach (var (fileName, content) in files)
            InstallFile(new FileInfo(Path.Combine(folder.FullName, fileName)), content, fileHashes[fileName]);

        return folder.FullName;
    }

    // Written under a name of its own and then moved into place, so neither a racing process nor a
    // killed one leaves a part in place; a file in place is never overwritten, only checked by its hash.
    private static void InstallFile(FileInfo file, Stream content, byte[] hash)
    {
        if (!file.Exists) {
            var tempFilePath = $"{file.FullName}.{Guid.NewGuid():N}.tmp";
            try {
                WriteNewFile(new FileInfo(tempFilePath), content);
                File.Move(tempFilePath, file.FullName, overwrite: false);
            }
            catch (IOException) when (File.Exists(file.FullName)) {
                // another process put it in place first
            }
            finally {
                File.Delete(tempFilePath); // nothing left once moved
            }

            file.Refresh();
        }

        if (!IsProtected(file) || !hash.AsSpan().SequenceEqual(GetSha256(file)))
            throw new InvalidOperationException(
                $"{file.FullName} is not the file installed there, is a link, or not only administrators may change it. " +
                "Delete its folder to have it made again.");
    }

    private static void WriteNewFile(FileInfo file, Stream content)
    {
        using var stream = file.Create(FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096,
            FileOptions.None, CreateSecurity(new FileSecurity(), InheritanceFlags.None));
        content.Position = 0;
        content.CopyTo(stream);
        stream.Flush(flushToDisk: true);
    }

    // Its own rules, none inherited: SYSTEM and administrators change it, the others read and run it.
    private static T CreateSecurity<T>(T security, InheritanceFlags inheritance) where T : FileSystemSecurity
    {
        security.SetOwner(Administrators);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(LocalSystem, FileSystemRights.FullControl, inheritance,
            PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, inheritance,
            PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, inheritance,
            PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    // No reparse point, owned by SYSTEM, administrators or TrustedInstaller, and no rule that lets
    // anyone else change it.
    private static bool IsProtected(FileSystemInfo item)
    {
        if (item.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return false;

        const AccessControlSections sections = AccessControlSections.Owner | AccessControlSections.Access;
        FileSystemSecurity security = item switch {
            DirectoryInfo directory => directory.GetAccessControl(sections),
            FileInfo file => file.GetAccessControl(sections),
            _ => throw new ArgumentException($"Unexpected file system item: {item.FullName}", nameof(item))
        };

        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !IsTrusted(owner))
            return false;

        return security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .All(rule => rule.AccessControlType == AccessControlType.Deny ||
                         rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly) ||
                         IsTrusted((SecurityIdentifier)rule.IdentityReference) ||
                         ((int)rule.FileSystemRights & UnsafeRights) == 0);
    }

    private static bool IsTrusted(SecurityIdentifier sid)
    {
        return TrustedSids.Contains(sid);
    }

    // Over the files' names and hashes: a new build of any file gets a new folder.
    private static string GetFolderHash(IReadOnlyDictionary<string, byte[]> fileHashes)
    {
        var manifest = new StringBuilder();
        foreach (var (fileName, hash) in fileHashes.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            manifest.Append(fileName.ToLowerInvariant()).Append(':')
                .Append(Convert.ToHexStringLower(hash)).Append('\n');

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifest.ToString())));
    }

    private static byte[] GetSha256(FileInfo file)
    {
        using var stream = file.OpenRead();
        return SHA256.HashData(stream);
    }

    private static byte[] GetSha256(Stream stream)
    {
        stream.Position = 0;
        return SHA256.HashData(stream);
    }
}

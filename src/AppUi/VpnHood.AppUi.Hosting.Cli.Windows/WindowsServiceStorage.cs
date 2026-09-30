using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32;

namespace VpnHood.AppUi.Hosting.Cli.Windows;

// The service's storage under ProgramData, which only SYSTEM and Administrators may open - not what
// ProgramData passes down, which lets any user add files, and the service runs what it stages here
// (the update, hosting plan §4.5).
// Anyone may make a folder in ProgramData, and the app id is public, so what the service finds under
// its name may not be its own. A link, a file, or a folder whose owner is not SYSTEM or Administrators
// is moved aside and a fresh folder made, so nothing planted in it is read. One held open, which not
// even SYSTEM can move, is passed over for a fresh folder under a name nobody can guess, recorded in
// the app's HKLM key, which only administrators write; each start looks there first. The access list
// is made right on every start, so a folder an earlier build made, or someone loosened, is brought back.
// What it moves or passes over is written to stderr, which a service's run sends to the Event Log.
internal static class WindowsServiceStorage
{
    private const string StoragePathValueName = "StoragePath";

    // The folder the service keeps its storage in from now on.
    public static string Prepare(WindowsCliPaths paths)
    {
        var path = paths.StoragePath;
        if (TryClaim(path))
            return path;

        var freshPath = $"{paths.DefaultStoragePath}-{RandomNumberGenerator.GetHexString(16, lowercase: true)}";
        if (!TryClaim(freshPath))
            throw new InvalidOperationException($"Could not make a storage folder: {freshPath}");

        Record(paths.RegistryKeyPath, freshPath);
        Console.Error.WriteLine($"The storage is now {freshPath}, since {path} could not be moved aside.");
        return freshPath;
    }

    // Where an earlier start went instead of its own folder; null while it never had to.
    public static string? ReadRecorded(string registryKeyPath)
    {
        using var key = Registry.LocalMachine.OpenSubKey(registryKeyPath);
        return key?.GetValue(StoragePathValueName) is string { Length: > 0 } path ? path : null;
    }

    private static void Record(string registryKeyPath, string path)
    {
        using var key = Registry.LocalMachine.CreateSubKey(registryKeyPath);
        key.SetValue(StoragePathValueName, path);
    }

    // Makes the folder the service's, or says it could not: what is there and not the service's is
    // moved aside first, and a folder made is made with the service's access list from the start.
    private static bool TryClaim(string path)
    {
        if (Path.Exists(path) && !IsServices(path) && !TryMoveAside(path))
            return false;

        var security = CreateSecurity();
        var directory = new DirectoryInfo(path);
        directory.Create(security); // nothing, where it is already

        // someone else's again, made between the move and this
        if (!IsServices(path))
            return false;

        var expected = security.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        if (directory.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access) != expected)
            directory.SetAccessControl(security);

        return true;
    }

    // A plain folder whose owner a standard user cannot set: SYSTEM or Administrators. Anything the
    // service cannot even read the owner of is not its own.
    private static bool IsServices(string path)
    {
        try {
            var attributes = File.GetAttributes(path);
            if (!attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint))
                return false;

            var owner = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner)
                .GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            return owner != null &&
                   (owner.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
                    owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return false;
        }
    }

    // Out of the way under a name of its own, where nothing reads it; false when it is held open.
    private static bool TryMoveAside(string path)
    {
        var movedPath = $"{path}.moved-{RandomNumberGenerator.GetHexString(8, lowercase: true)}";
        try {
            if (File.GetAttributes(path).HasFlag(FileAttributes.Directory))
                Directory.Move(path, movedPath);
            else
                File.Move(path, movedPath);

            Console.Error.WriteLine($"{path} was not the service's, so it was moved to {movedPath}.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            Console.Error.WriteLine($"{path} is not the service's and could not be moved aside: {ex.Message}");
            return false;
        }
    }

    private static DirectorySecurity CreateSecurity()
    {
        const InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }
}

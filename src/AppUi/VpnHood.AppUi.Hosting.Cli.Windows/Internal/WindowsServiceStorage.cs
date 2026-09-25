using System.Security.AccessControl;
using System.Security.Principal;

namespace VpnHood.AppUi.Hosting.Cli.Windows.Internal;

// The service's storage under ProgramData, with an ACL of its own: SYSTEM and Administrators change
// it, and everyone else only reads it - the window reads daemon.json, "service log" reads app.log.
// Not what ProgramData passes down, which lets any user add files: the service runs what it stages
// here (the update, hosting plan §4.5), so a file nobody else could have written is its check. Made
// right on every start, so a folder an earlier build made, or someone loosened, is brought back.
internal static class WindowsServiceStorage
{
    public static void Secure(string storagePath)
    {
        var directory = Directory.CreateDirectory(storagePath);
        var security = CreateSecurity();
        var expected = security.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        if (directory.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access) == expected)
            return;

        directory.SetAccessControl(security);
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
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute, inheritance, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }
}
